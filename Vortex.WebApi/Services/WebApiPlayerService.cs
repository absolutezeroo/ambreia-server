using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Rooms.Enums;
using Vortex.WebApi.Configuration;
using Vortex.WebApi.Http;

namespace Vortex.WebApi.Services;

public sealed class WebApiPlayerService(
    IDbContextFactory<VortexDbContext> dbCtxFactory,
    IGrainFactory grainFactory,
    IOptions<WebApiConfig> options,
    ILogger<WebApiPlayerService> logger
) : IWebApiPlayerService
{
    private readonly IDbContextFactory<VortexDbContext> _db = dbCtxFactory;
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly WebApiConfig _config = options.Value;
    private readonly ILogger<WebApiPlayerService> _logger = logger;

    public async Task<List<AvatarInfo>> GetAvatarsForAccountAsync(
        int accountId,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await _db.CreateDbContextAsync(ct).ConfigureAwait(false);

        List<PlayerEntity> players = await db
            .Players.AsNoTracking()
            .Where(p => p.PlayerAccountEntityId == accountId && p.DeletedAt == null)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return players.Select(ToAvatarInfo).ToList();
    }

    public async Task<(bool Success, int PlayerId, string? Error)> CreateAvatarAsync(
        int accountId,
        string name,
        string figure,
        string gender,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await _db.CreateDbContextAsync(ct).ConfigureAwait(false);

        // A blank name means the caller is registering: the client posts to this route straight
        // after /api/public/registration/new, and the avatar is named later by the onboarding step
        // that AuthenticationOK asks for (AVATAR_NAME_CHANGE is sent for as long as the player has
        // no NuxCompletedAt, and ChangeUserNameMessageHandler is what sets it). The player row still
        // has to exist before then — there is no ticket, and so no connection, without one — so it
        // is given a placeholder. Player.Name is uniquely indexed, so the placeholder embeds a GUID
        // rather than a counter: it cannot collide with another registration racing it, nor with a
        // name a player picks.
        bool named = !string.IsNullOrWhiteSpace(name);

        if (!named)
        {
            name = $"New user {Guid.NewGuid():N}"[..24];
        }
        else if (!NameShape.IsWellFormed(name))
        {
            // Only a name the caller actually supplied is checked: the placeholder above is
            // deliberately outside the policy (it has a space and is 24 long) precisely so it cannot
            // collide with anything a player is allowed to pick, and the onboarding rename replaces
            // it through SetNameAsync, which does enforce the policy.
            _logger.LogWarning("Avatar creation refused: name '{Name}' is not well formed", name);
            return (false, 0, "pocket.auth.name_not_valid");
        }

        int count = await db
            .Players.AsNoTracking()
            .CountAsync(p => p.PlayerAccountEntityId == accountId && p.DeletedAt == null, ct)
            .ConfigureAwait(false);

        if (count >= _config.MaxAvatarsPerAccount)
        {
            _logger.LogWarning(
                "Avatar creation refused: account {AccountId} reached max avatars ({Max})",
                accountId,
                _config.MaxAvatarsPerAccount
            );
            return (false, 0, "pocket.auth.max_avatars_reached");
        }

        bool taken = await db
            .Players.AsNoTracking()
            .AnyAsync(p => p.Name == name, ct)
            .ConfigureAwait(false);

        if (taken)
        {
            _logger.LogWarning("Avatar creation refused: name '{Name}' already taken", name);
            return (false, 0, "pocket.auth.name_taken");
        }

        AvatarGenderType genderType = AvatarGenderTypeExtensions.FromLegacyString(gender);

        PlayerEntity player = new PlayerEntity
        {
            PlayerAccountEntityId = accountId,
            Name = name,
            Figure = string.IsNullOrWhiteSpace(figure) ? _config.DefaultFigure : figure,
            Gender = genderType,
            PlayerStatus = PlayerStatusType.Offline,
            PlayerPerks = PlayerPerkFlags.None,
            // A caller who supplied a name has already been through the naming step — the site's
            // "create an avatar" modal asks for one, checks it as you type, and posts it. Leaving
            // NuxCompletedAt null meant AuthenticationOK asked for AVATAR_NAME_CHANGE anyway and the
            // client opened the rename dialog over a player who had just chosen their name.
            //
            // The placeholder branch above is the opposite case and must stay null: a registration
            // names its avatar in the client, which is the whole reason the placeholder exists.
            NuxCompletedAt = named ? DateTime.UtcNow : null,
        };

        db.Players.Add(player);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Avatar '{Name}' created for account {AccountId} (playerId={PlayerId})",
            name,
            accountId,
            player.Id
        );
        return (true, player.Id, null);
    }

    public async Task<bool> NameAvailableAsync(string name, CancellationToken ct)
    {
        await using VortexDbContext db = await _db.CreateDbContextAsync(ct).ConfigureAwait(false);
        return !await db.Players.AnyAsync(p => p.Name == name, ct).ConfigureAwait(false);
    }

    public async Task<bool> SetNameAsync(int playerId, string name, CancellationToken ct)
    {
        // The in-game rename runs this same check (ChangeUserNameMessageHandler); this route reaches
        // the identical grain call, so without it the HTTP path is a second door onto the same sink
        // with no lock on it. A name is public identity — whisper, gifting, friend requests and
        // LetUserIn all resolve by exact string — so one that the client's own dialog would refuse
        // is a working impersonation.
        if (!NameShape.IsWellFormed(name))
        {
            return false;
        }

        await using VortexDbContext db = await _db.CreateDbContextAsync(ct).ConfigureAwait(false);

        bool taken = await db
            .Players.AsNoTracking()
            .AnyAsync(p => p.Name == name && p.Id != playerId, ct)
            .ConfigureAwait(false);

        if (taken)
        {
            return false;
        }

        bool exists = await db
            .Players.AsNoTracking()
            .AnyAsync(p => p.Id == playerId, ct)
            .ConfigureAwait(false);

        if (!exists)
        {
            return false;
        }

        await _grainFactory.GetPlayerGrain(playerId).SetNameAsync(name, ct).ConfigureAwait(false);
        return true;
    }

    private static AvatarInfo ToAvatarInfo(PlayerEntity p) =>
        new(
            UniqueId: p.Id.ToString(),
            Name: p.Name,
            Motto: p.Motto ?? string.Empty,
            FigureString: p.Figure,
            Gender: p.Gender.ToLegacyString()
        );
}
