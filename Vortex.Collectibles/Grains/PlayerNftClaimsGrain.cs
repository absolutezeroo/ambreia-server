using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Entities.Collectibles;
using Vortex.Primitives.Collectibles;
using Vortex.Primitives.Collectibles.Grains;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Events;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Inventory.Grains;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;

namespace Vortex.Collectibles.Grains;

/// <summary>
/// One player's Relics: the prizes waiting in the Collectors Guild, and handing them over.
/// </summary>
/// <remarks>
/// <para>
/// Two different things arrive on the same tab, because the client has one. A <c>nft_claims</c> row
/// is a prize an admin wrote down — "this player may take this piece of furniture" — and claiming it
/// grants the furniture. A <b>returned Relic</b> is the other door: one that left through the
/// Transfer tab and has since been sent back to the hotel's treasury on the chain. Claiming that one
/// burns the token and puts the Relic back in the player's collection, as a Relic and not as
/// furniture.
/// </para>
/// <para>
/// <b>A returned Relic goes to whoever last held it in the hotel</b>, which is what
/// <c>nft_assets.player_id</c> records — not to whoever sent it to the treasury, because the chain
/// does not say who that was: <c>tokensOfOwner</c> answers a list of ids and no history. The
/// consequence is worth stating plainly rather than discovering: somebody who buys a Relic from a
/// stranger on the chain and sends it to the treasury has given it back to the person they bought it
/// from. Knowing the sender needs either a deposit address per player, funded with gas so the hotel
/// can burn from it, or an approval the buyer grants the treasury first. Neither exists yet, and
/// neither is worth building ahead of a hotel that has a marketplace.
/// </para>
/// </remarks>
internal sealed class PlayerNftClaimsGrain(
    IDbContextFactory<VortexDbContext> dbCtxFactory,
    IGrainFactory grainFactory,
    IEventPublisher events,
    ILogger<PlayerNftClaimsGrain> logger
) : Grain, IPlayerNftClaimsGrain
{
    private readonly IDbContextFactory<VortexDbContext> _dbCtxFactory = dbCtxFactory;
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly IEventPublisher _events = events;
    private readonly ILogger<PlayerNftClaimsGrain> _logger = logger;

    /// <summary>
    /// What a returned Relic's claim id is built from. The client only echoes the id back, and the
    /// prefix is what keeps it from ever colliding with an admin's <c>claim_code</c>.
    /// </summary>
    private const string RETURNED_RELIC_PREFIX = "relic:";

    private PlayerId PlayerId => new((int)this.GetPrimaryKeyLong());

    public async Task<ImmutableArray<NftClaimSnapshot>> GetClaimsAsync(
        string wallet,
        CancellationToken ct
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(true);

        List<NftClaimEntity> claims = await OutstandingAsync(dbCtx, ct).ConfigureAwait(true);

        // Scoped to the wallet asked about, so a Relic shows up on exactly one of the tab's per-
        // wallet lists: the one it left from.
        List<NftAssetEntity> returned = await ReturnedRelicsAsync(dbCtx, wallet, ct)
            .ConfigureAwait(true);

        if (claims.Count == 0 && returned.Count == 0)
        {
            return [];
        }

        Dictionary<string, FurnitureIdentity> definitions = await LoadDefinitionsAsync(
                dbCtx,
                [
                    .. claims.Select(claim => claim.ProductCode),
                    .. returned.Select(asset => asset.ProductCode),
                ],
                ct
            )
            .ConfigureAwait(true);

        return
        [
            .. claims.Select(claim => ToSnapshot(claim, wallet, definitions)),
            .. returned.Select(asset => ToSnapshot(asset, wallet, definitions)),
        ];
    }

    public async Task<int> ClaimAllAsync(CancellationToken ct)
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(true);

        // Tracked, not AsNoTracking: the counters below are written back.
        List<NftClaimEntity> claims = await OutstandingAsync(dbCtx, ct).ConfigureAwait(true);

        // Every wallet, not one: the client's button carries no arguments at all, so "claim
        // everything" has to mean everything.
        int returned = await ClaimReturnedRelicsAsync(dbCtx, ct).ConfigureAwait(true);

        if (claims.Count == 0)
        {
            return returned;
        }

        Dictionary<string, FurnitureIdentity> definitions = await LoadDefinitionsAsync(
                dbCtx,
                [.. claims.Select(claim => claim.ProductCode)],
                ct
            )
            .ConfigureAwait(true);

        IInventoryGrain inventory = _grainFactory.GetInventoryGrain(PlayerId);
        int granted = 0;

        foreach (NftClaimEntity claim in claims)
        {
            if (!definitions.TryGetValue(claim.ProductCode, out FurnitureIdentity definition))
            {
                // A claim naming furniture that does not exist would consume the entitlement and
                // hand over nothing, so it is skipped and left outstanding for an admin to fix.
                _logger.LogError(
                    "Claim {ClaimCode} for player {PlayerId} names unknown furniture {ProductCode}; skipped.",
                    claim.ClaimCode,
                    PlayerId,
                    claim.ProductCode
                );

                continue;
            }

            int remaining = claim.ClaimLimit - claim.ClaimedAmount;

            if (remaining <= 0)
            {
                continue;
            }

            // One receipted call for the whole claim, under an id derived from the claim row.
            //
            // It used to be one bare call per copy, with every counter saved after the loop. A
            // second copy that failed -- or the save at the end -- threw out of the whole method
            // with the first copy already durably in the inventory and the entitlement still
            // outstanding, so the next press of the button handed it over again. The entitlement
            // was the only thing counting, and it was counted last.
            //
            // The receipt commits in the same transaction as the furniture rows, so a replay is
            // answered from it and grants nothing.
            await inventory
                .GrantFurnitureDefinitionCopiesAsync(
                    definition.DefinitionId,
                    null,
                    remaining,
                    CommerceOperationId.Deterministic(CommerceOperationKind.NftClaim, claim.Id),
                    CommerceStepKeys.LOCAL_GRANT,
                    ct
                )
                .ConfigureAwait(true);

            granted += remaining;

            // Per claim, not once for all of them at the end: a failure on the next claim leaves
            // this one's counter written, and its receipt means a retry cannot hand it over twice
            // even if the counter is what failed.
            claim.ClaimedAmount = claim.ClaimLimit;
            await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);
        }

        _logger.LogInformation(
            "Player {PlayerId} claimed {Granted} collectible reward(s).",
            PlayerId,
            granted
        );

        if (granted > 0)
        {
            await _events
                .PublishAsync(new NftClaimsCollectedEvent(PlayerId, granted), ct)
                .ConfigureAwait(true);
        }

        return granted + returned;
    }

    // The chain is gone: no Relic ever comes home from a treasury.
    private static Task<List<NftAssetEntity>> ReturnedRelicsAsync(
        VortexDbContext dbCtx,
        string wallet,
        CancellationToken ct
    ) => Task.FromResult<List<NftAssetEntity>>([]);

    private static Task<int> ClaimReturnedRelicsAsync(
        VortexDbContext dbCtx,
        CancellationToken ct
    ) => Task.FromResult(0);

    /// <summary>
    /// A returned Relic as the Rewards tab lists it: one item, claimable once, no expiry.
    /// </summary>
    private static NftClaimSnapshot ToSnapshot(
        NftAssetEntity asset,
        string wallet,
        Dictionary<string, FurnitureIdentity> definitions
    )
    {
        definitions.TryGetValue(asset.ProductCode, out FurnitureIdentity definition);

        return new NftClaimSnapshot
        {
            ClaimId = RETURNED_RELIC_PREFIX + asset.Id.ToString(CultureInfo.InvariantCulture),
            Status = 0,
            ClaimedAmount = 0,
            ClaimLimit = 1,
            ValidFrom = 0,
            ValidTo = 0,
            CreatedAt = ToUnixMs(asset.CreatedAt),
            UpdatedAt = ToUnixMs(asset.UpdatedAt),
            Collection = string.Empty,
            ProductCode = asset.ProductCode,
            Wallet = wallet,
            ClaimItem = new NftClaimItemSnapshot
            {
                Product = new CollectibleProductItemSnapshot
                {
                    ProductTypeId = CollectibleProductIdentity.ForFurniture(definition.ProductType),
                    ItemTypeId = CollectibleProductIdentity.ItemTypeId(definition.SpriteId),
                    Score = 0,
                    ProductCode = asset.ProductCode,
                },
                SetId = string.Empty,
                DefaultCollectionName = string.Empty,
            },
        };
    }

    /// <summary>
    /// The claims a player may actually take: outstanding, and within the collector level they have
    /// reached.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both doors go through here — listing the tab and pressing its button — because a prize the
    /// list hides and the button hands over anyway is not gated at all.
    /// </para>
    /// <para>
    /// The level is only counted when a claim actually asks for one. Computing it is a pass over
    /// everything the player owns, and a hotel that gates nothing should not pay for that on every
    /// open of the tab.
    /// </para>
    /// </remarks>
    private async Task<List<NftClaimEntity>> OutstandingAsync(
        VortexDbContext dbCtx,
        CancellationToken ct
    )
    {
        List<NftClaimEntity> claims = await OutstandingQuery(dbCtx)
            .OrderBy(claim => claim.Id)
            .ToListAsync(ct)
            .ConfigureAwait(true);

        if (!claims.Exists(claim => claim.MinCollectorLevel > 0))
        {
            return claims;
        }

        CollectorScoreSnapshot score = await _grainFactory
            .GetNftCollectionsGrain()
            .GetCollectorScoreAsync(PlayerId, ct)
            .ConfigureAwait(true);

        return [.. claims.Where(claim => claim.MinCollectorLevel <= score.Level)];
    }

    /// <summary>
    /// The claims a player may still take: not deleted, not already exhausted, and inside their
    /// validity window when they have one.
    /// </summary>
    private IQueryable<NftClaimEntity> OutstandingQuery(VortexDbContext dbCtx)
    {
        DateTime now = DateTime.UtcNow;

        return dbCtx.NftClaims.Where(claim =>
            claim.PlayerEntityId == PlayerId.Value
            && claim.DeletedAt == null
            && claim.ClaimedAmount < claim.ClaimLimit
            && (claim.ValidFrom == null || claim.ValidFrom <= now)
            && (claim.ValidTo == null || claim.ValidTo >= now)
        );
    }

    private static async Task<Dictionary<string, FurnitureIdentity>> LoadDefinitionsAsync(
        VortexDbContext dbCtx,
        string[] productCodes,
        CancellationToken ct
    )
    {
        return await FurnitureDefinitionLookup
            .ResolveByClassNameAsync(
                dbCtx,
                productCodes,
                definition => new FurnitureIdentity(
                    definition.Id,
                    definition.SpriteId,
                    definition.ProductType
                ),
                ct
            )
            .ConfigureAwait(true);
    }

    private static NftClaimSnapshot ToSnapshot(
        NftClaimEntity claim,
        string wallet,
        Dictionary<string, FurnitureIdentity> definitions
    )
    {
        definitions.TryGetValue(claim.ProductCode, out FurnitureIdentity definition);

        return new NftClaimSnapshot
        {
            ClaimId = claim.ClaimCode,
            Status = claim.Status,
            ClaimedAmount = claim.ClaimedAmount,
            ClaimLimit = claim.ClaimLimit,
            ValidFrom = ToUnixMs(claim.ValidFrom),
            ValidTo = ToUnixMs(claim.ValidTo),
            CreatedAt = ToUnixMs(claim.CreatedAt),
            UpdatedAt = ToUnixMs(claim.UpdatedAt),
            Collection = claim.Collection,
            ProductCode = claim.ProductCode,
            Wallet = wallet,
            ClaimItem = new NftClaimItemSnapshot
            {
                Product = new CollectibleProductItemSnapshot
                {
                    ProductTypeId = CollectibleProductIdentity.ForFurniture(definition.ProductType),
                    ItemTypeId = CollectibleProductIdentity.ItemTypeId(definition.SpriteId),
                    Score = 0,
                    ProductCode = claim.ProductCode,
                },
                SetId = claim.SetId,
                DefaultCollectionName = claim.DefaultCollectionName,
            },
        };
    }

    /// <summary>The client builds a <c>Date</c> straight from these, so they are milliseconds.</summary>
    private static long ToUnixMs(DateTime? value) =>
        value is null
            ? 0
            : new DateTimeOffset(
                DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
            ).ToUnixTimeMilliseconds();

    private readonly record struct FurnitureIdentity(
        int DefinitionId,
        int SpriteId,
        ProductType ProductType
    );
}
