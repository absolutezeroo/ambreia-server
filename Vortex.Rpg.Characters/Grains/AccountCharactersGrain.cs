using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Avatar;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Primitives.Rpg.Characters.Grains;
using Vortex.Protocol.Messages.Outgoing.Rpg.Characters;
using Vortex.Rpg.Characters.Configuration;
using Vortex.Rpg.Content;
using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Characters.Grains;

/// <inheritdoc cref="IAccountCharactersGrain"/>
internal sealed class AccountCharactersGrain(
    IDbContextFactory<VortexDbContext> dbFactory,
    IRpgContent content,
    IGrainFactory grainFactory,
    IOptions<CharacterConfig> config,
    ILogger<AccountCharactersGrain> logger
) : Grain, IAccountCharactersGrain
{
    private const string Ok = RpgCharacterResultMessageComposer.Ok;

    private readonly CharacterConfig _config = config.Value;

    /// <summary>
    /// Creates started and not finished. Orleans runs one call here at a time, so this is 0 for
    /// every create but under reentrancy; it is taken before the first await so the limit holds
    /// even then.
    /// </summary>
    private int _creating;

    private int AccountId => (int)this.GetPrimaryKeyLong();

    private IAccountSessionGrain Session => grainFactory.GetAccountSessionGrain(AccountId);

    public async Task OpenAsync()
    {
        List<PlayerEntity> avatars = await AvatarsAsync();
        CharacterSummary?[] summaries = await SummariesAsync(avatars);

        // An avatar from before the RPG has no character: it starts where a new one does.
        PlayerEntity[] bare = [.. avatars.Where((_, i) => summaries[i] is null)];

        if (bare.Length > 0 && StarterClassId() is { } starter)
        {
            await Task.WhenAll(bare.Select(p => GiveStarterAsync(p.Id, starter)));
            summaries = await SummariesAsync(avatars);
        }

        await Session.SendComposerAsync(BuildList(avatars, summaries), Classes());
    }

    public async Task ListAsync() => await Session.SendComposerAsync(await CurrentListAsync());

    public async Task CreateAsync(string name, string figure, string gender)
    {
        (string code, int playerId) = (RpgCharacterResultMessageComposer.Failed, 0);

        try
        {
            (code, playerId) = await CreateCoreAsync(name, figure, gender);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Character creation failed for account {AccountId}", AccountId);
        }

        await AnswerAsync(RpgCharacterResultMessageComposer.ActionCreate, code, playerId);
    }

    public async Task ChooseClassAsync(int playerId, string classId)
    {
        string code = RpgCharacterResultMessageComposer.Failed;

        try
        {
            code = !await OwnedAsync(playerId)
                ? "character.not_owned"
                : await grainFactory
                    .GetPlayerCharacterGrain(playerId)
                    .ChooseClassAsync(classId) switch
                {
                    ChooseClassOutcome.Chosen => Ok,
                    ChooseClassOutcome.AlreadyChosen => "character.class_already_chosen",
                    _ => "character.unknown_class",
                };

            logger.LogInformation(
                "Class choice {ClassId} for player {PlayerId} of account {AccountId}: {Code}",
                classId,
                playerId,
                AccountId,
                code
            );
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Class choice failed for player {PlayerId} of account {AccountId}",
                playerId,
                AccountId
            );
        }

        await AnswerAsync(RpgCharacterResultMessageComposer.ActionChooseClass, code, playerId);
    }

    public async Task<bool> OwnsAsync(int playerId)
    {
        if (await OwnedAsync(playerId))
        {
            return true;
        }

        await AnswerAsync(
            RpgCharacterResultMessageComposer.ActionEnterWorld,
            "character.not_owned",
            playerId
        );

        return false;
    }

    private async Task<(string Code, int PlayerId)> CreateCoreAsync(
        string name,
        string figure,
        string gender
    )
    {
        if (!NameShape.IsWellFormed(name))
        {
            return ("character.name_invalid", 0);
        }

        figure = string.IsNullOrWhiteSpace(figure) ? _config.DefaultFigure : figure;

        if (figure.Length > FigureString.MaxLength)
        {
            return ("character.figure_invalid", 0);
        }

        int ahead = _creating++;

        try
        {
            await using VortexDbContext db = await dbFactory.CreateDbContextAsync();

            int count = await db.Players.CountAsync(p =>
                p.PlayerAccountEntityId == AccountId && p.DeletedAt == null
            );

            if (count + ahead >= _config.MaxCharactersPerAccount)
            {
                return ("character.limit_reached", 0);
            }

            if (await db.Players.AnyAsync(p => p.Name == name))
            {
                return ("character.name_taken", 0);
            }

            PlayerEntity player = new()
            {
                PlayerAccountEntityId = AccountId,
                Name = name,
                Figure = figure,
                Gender = AvatarGenderTypeExtensions.FromLegacyString(gender),
                PlayerStatus = PlayerStatusType.Offline,
                PlayerPerks = PlayerPerkFlags.None,
            };
            db.Players.Add(player);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Taken by another account between the check and the write: the unique index.
                // Anything else is unexpected, and CreateAsync logs it.
                if (await NameTakenAsync(name))
                {
                    return ("character.name_taken", 0);
                }

                throw;
            }

            // The row is written: the character exists whatever happens to its class, and the next
            // opening of the screen gives the starter again.
            if (StarterClassId() is { } starter)
            {
                await GiveStarterAsync(player.Id, starter);
            }

            logger.LogInformation(
                "Character {PlayerId} '{Name}' created for account {AccountId}",
                player.Id,
                name,
                AccountId
            );

            return (Ok, player.Id);
        }
        finally
        {
            _creating--;
        }
    }

    /// <summary>« Sans classe » for an avatar without a character; a failure is logged and the
    /// avatar stays one to give it to, at the next opening.</summary>
    private async Task GiveStarterAsync(int playerId, string starter)
    {
        try
        {
            await grainFactory.GetPlayerCharacterGrain(playerId).ChooseClassAsync(starter);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Could not give the starter class to player {PlayerId}",
                playerId
            );
        }
    }

    private async Task<bool> NameTakenAsync(string name)
    {
        await using VortexDbContext db = await dbFactory.CreateDbContextAsync();

        return await db.Players.AnyAsync(p => p.Name == name);
    }

    private async Task<bool> OwnedAsync(int playerId)
    {
        await using VortexDbContext db = await dbFactory.CreateDbContextAsync();

        return await db.Players.AnyAsync(p =>
            p.Id == playerId && p.PlayerAccountEntityId == AccountId && p.DeletedAt == null
        );
    }

    /// <summary>The result, then the fresh list when it worked: one send, so in that order.</summary>
    private async Task AnswerAsync(int action, string code, int playerId)
    {
        RpgCharacterResultMessageComposer result = new()
        {
            Action = action,
            Code = code,
            PlayerId = playerId,
        };

        await Session.SendComposerAsync(code == Ok ? [result, await CurrentListAsync()] : [result]);
    }

    private async Task<RpgCharacterListMessageComposer> CurrentListAsync()
    {
        List<PlayerEntity> avatars = await AvatarsAsync();

        return BuildList(avatars, await SummariesAsync(avatars));
    }

    private async Task<List<PlayerEntity>> AvatarsAsync()
    {
        await using VortexDbContext db = await dbFactory.CreateDbContextAsync();

        return await db
            .Players.AsNoTracking()
            .Where(p => p.PlayerAccountEntityId == AccountId && p.DeletedAt == null)
            .OrderBy(p => p.Id)
            .ToListAsync();
    }

    private Task<CharacterSummary?[]> SummariesAsync(List<PlayerEntity> avatars) =>
        Task.WhenAll(avatars.Select(p => SummaryOrNullAsync(p.Id)));

    /// <summary>One broken character grain shows as a character to choose, not as no list.</summary>
    private async Task<CharacterSummary?> SummaryOrNullAsync(int playerId)
    {
        try
        {
            return await grainFactory.GetPlayerCharacterGrain(playerId).GetSummaryAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No character summary for player {PlayerId}", playerId);

            return null;
        }
    }

    private RpgCharacterListMessageComposer BuildList(
        List<PlayerEntity> avatars,
        CharacterSummary?[] summaries
    ) =>
        new()
        {
            MaxCharacters = _config.MaxCharactersPerAccount,
            Characters =
            [
                .. avatars.Select(
                    (p, i) =>
                        new RpgCharacterListEntry
                        {
                            PlayerId = p.Id,
                            Name = p.Name,
                            Figure = p.Figure,
                            Gender = p.Gender.ToLegacyString(),
                            ClassId = summaries[i]?.ClassId ?? string.Empty,
                            SpecializationId = summaries[i]?.SpecializationId ?? string.Empty,
                            Level = summaries[i]?.Level ?? 0,
                            Starter =
                                summaries[i] is not { } s
                                || content.Get<ClassDefinition>(s.ClassId) is { Starter: true },
                        }
                ),
            ],
        };

    private RpgClassListMessageComposer Classes() =>
        new()
        {
            Classes =
            [
                .. content
                    .All<ClassDefinition>()
                    .Where(c => !c.Retired)
                    .OrderBy(c => c.Id, StringComparer.Ordinal)
                    .Select(c => new RpgClassEntry
                    {
                        Id = c.Id,
                        Role = JsonNamingPolicy.SnakeCaseLower.ConvertName(c.Role.ToString()),
                        Range = c.Range,
                        PrimaryStat = JsonNamingPolicy.SnakeCaseLower.ConvertName(
                            c.PrimaryStat.ToString()
                        ),
                        Starter = c.Starter,
                        Specializations = [.. c.Specializations],
                    }),
            ],
        };

    private string? StarterClassId() =>
        content.All<ClassDefinition>().FirstOrDefault(c => c.Starter && !c.Retired)?.Id;
}
