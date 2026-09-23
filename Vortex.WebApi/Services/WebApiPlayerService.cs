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
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Rooms.Enums;
using Vortex.WebApi.Configuration;
using Vortex.WebApi.Http;

namespace Vortex.WebApi.Services;

public sealed class WebApiPlayerService(
    IDbContextFactory<VortexDbContext> dbCtxFactory,
    IGrainFactory grainFactory,
    IWordFilter wordFilter,
    IOptions<WebApiConfig> options,
    ILogger<WebApiPlayerService> logger
) : IWebApiPlayerService
{
    private readonly IDbContextFactory<VortexDbContext> _db = dbCtxFactory;
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly IWordFilter _wordFilter = wordFilter;
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
        // Journaled against the ACCOUNT: there is no player yet. The same answer as a malformed name,
        // because to the page a forbidden name is a name it cannot have.
        else if (_wordFilter.Apply(name, WordFilterSource.Username, accountId).Refused)
        {
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
        // Test, not Apply: the page probes while the player types, like the in-game check does, and
        // journaling every probe would bury the journal. The claim is what journals.
        if (_wordFilter.Test(name).Hit)
        {
            return false;
        }

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
        if (
            !NameShape.IsWellFormed(name)
            || _wordFilter.Apply(name, WordFilterSource.Username, playerId).Refused
        )
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

    public async Task<bool> SaveFigureAsync(
        int playerId,
        string figureString,
        string gender,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await _db.CreateDbContextAsync(ct).ConfigureAwait(false);

        bool exists = await db
            .Players.AsNoTracking()
            .AnyAsync(p => p.Id == playerId, ct)
            .ConfigureAwait(false);

        if (!exists)
        {
            return false;
        }

        AvatarGenderType genderType = AvatarGenderTypeExtensions.FromLegacyString(gender);

        await _grainFactory
            .GetPlayerGrain(playerId)
            .SetFigureAsync(figureString, genderType, ct)
            .ConfigureAwait(false);

        return true;
    }

    public async Task<AvatarInfo?> GetAvatarAsync(int playerId, CancellationToken ct)
    {
        await using VortexDbContext db = await _db.CreateDbContextAsync(ct).ConfigureAwait(false);

        PlayerEntity? player = await db
            .Players.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == playerId, ct)
            .ConfigureAwait(false);

        return player is null ? null : ToAvatarInfo(player);
    }

    public async Task<bool?> GetProfileVisibleAsync(int playerId, CancellationToken ct)
    {
        await using VortexDbContext db = await _db.CreateDbContextAsync(ct).ConfigureAwait(false);

        return await db
            .Players.AsNoTracking()
            .Where(p => p.Id == playerId && p.DeletedAt == null)
            .Select(p => (bool?)p.ProfileVisible)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<bool> SetProfileVisibleAsync(int playerId, bool visible, CancellationToken ct)
    {
        await using VortexDbContext db = await _db.CreateDbContextAsync(ct).ConfigureAwait(false);

        // Written straight to the column rather than through the player's grain, unlike the rename
        // next to it: nothing in the game reads this — a room shows who is in it either way — so
        // there is no live state to keep in step, and waking a grain to set a web preference would
        // be load for nothing. The grain's own writes are targeted `ExecuteUpdate`s on the columns
        // it owns, so neither side can overwrite the other.
        int rows = await db
            .Players.Where(p => p.Id == playerId && p.DeletedAt == null)
            .ExecuteUpdateAsync(up => up.SetProperty(p => p.ProfileVisible, visible), ct)
            .ConfigureAwait(false);

        return rows > 0;
    }

    public async Task<PlayerPurse?> GetPurseAsync(int playerId, CancellationToken ct)
    {
        await using VortexDbContext db = await _db.CreateDbContextAsync(ct).ConfigureAwait(false);

        bool exists = await db
            .Players.AsNoTracking()
            .AnyAsync(p => p.Id == playerId && p.DeletedAt == null, ct)
            .ConfigureAwait(false);

        if (!exists)
        {
            return null;
        }

        // A currency row only exists once the player has held that currency, so the wallet is read
        // as a dictionary and missing rows are zero rather than an absent counter.
        //
        // Only the THREE this purse shows, and that narrowing IS the fix for a 500 this route
        // answered on any hotel carrying more than one activity-point currency. A wallet is keyed
        // by <see cref="CurrencyKind" />, the PAIR (CurrencyType, ActivityPointType) — so
        // <c>CurrencyType.ActivityPoints</c> is a FAMILY and not a currency: duckets, diamonds and
        // every seasonal currency each get their own <c>currency_types</c> row under it. This read
        // keyed on <c>CurrencyType</c> alone, collapsed the whole family onto one key, and
        // <c>ToDictionaryAsync</c> threw "An item with the same key has already been added" — while
        // building an entry for a value the purse does not display.
        //
        // `Max` and not `Sum` for the three that remain: the wallet grain spends from ONE row per
        // kind (<c>PlayerWalletGrain.ResolveCurrencyRowAsync</c> resolves a single id), so adding
        // two rows together would print a balance the game will not let the player spend.
        Dictionary<CurrencyType, int> balances = await db
            .PlayerCurrencies.AsNoTracking()
            .Where(c =>
                c.PlayerEntityId == playerId
                && c.CurrencyTypeEntity != null
                && (
                    c.CurrencyTypeEntity.CurrencyType == CurrencyType.Credits
                    || c.CurrencyTypeEntity.CurrencyType == CurrencyType.Silver
                    || c.CurrencyTypeEntity.CurrencyType == CurrencyType.Emeralds
                )
            )
            .GroupBy(c => c.CurrencyTypeEntity!.CurrencyType)
            .Select(group => new { Kind = group.Key, Amount = group.Max(c => c.Amount) })
            .ToDictionaryAsync(row => row.Kind, row => row.Amount, ct)
            .ConfigureAwait(false);

        DateTime now = DateTime.UtcNow;

        List<PlayerSubscriptionEntity> subscriptions = await db
            .PlayerSubscriptions.AsNoTracking()
            .Where(s => s.PlayerEntityId == playerId && s.ExpiresAt > now)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        PlayerSubscriptionEntity? club = subscriptions.Find(s =>
            s.SubscriptionType == SubscriptionType.HabboClub
        );
        PlayerSubscriptionEntity? builders = subscriptions.Find(s =>
            s.SubscriptionType == SubscriptionType.BuildersClub
        );

        // Rounded UP: a subscription with eight hours left is a day left, not zero days, which is
        // what the counter says everywhere else in the hotel.
        int clubDays = club is null ? 0 : (int)Math.Ceiling((club.ExpiresAt - now).TotalDays);

        // The same count for the other membership. habbo.com's purse prints BOTH in days
        // (`SHOP_PURSE_HC_DAYS` / `SHOP_PURSE_BC_DAYS`); the furni limit is a different fact
        // altogether, and the website had been showing it under the days' label — which reads as a
        // wrong number rather than as another one.
        int buildersDays = builders is null
            ? 0
            : (int)Math.Ceiling((builders.ExpiresAt - now).TotalDays);

        int furniLimit = builders is null
            ? 0
            : await db
                .BuildersClubTiers.AsNoTracking()
                .Where(t => t.Level == builders.Level)
                .Select(t => t.FurniLimit)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

        return new PlayerPurse(
            balances.GetValueOrDefault(CurrencyType.Credits),
            balances.GetValueOrDefault(CurrencyType.Emeralds),
            balances.GetValueOrDefault(CurrencyType.Silver),
            clubDays,
            buildersDays,
            furniLimit
        );
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
