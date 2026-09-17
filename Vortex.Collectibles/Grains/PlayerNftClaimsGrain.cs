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
using Vortex.Primitives.Chain;
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
    ILogger<PlayerNftClaimsGrain> logger,
    IChainService chain
) : Grain, IPlayerNftClaimsGrain
{
    private readonly IDbContextFactory<VortexDbContext> _dbCtxFactory = dbCtxFactory;
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly IEventPublisher _events = events;
    private readonly ILogger<PlayerNftClaimsGrain> _logger = logger;
    private readonly IChainService _chain = chain;

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

        NftClaimEntity[] claims = await OutstandingQuery(dbCtx)
            .OrderBy(claim => claim.Id)
            .ToArrayAsync(ct)
            .ConfigureAwait(true);

        // Scoped to the wallet asked about, so a Relic shows up on exactly one of the tab's per-
        // wallet lists: the one it left from.
        List<NftAssetEntity> returned = await ReturnedRelicsAsync(dbCtx, wallet, ct)
            .ConfigureAwait(true);

        if (claims.Length == 0 && returned.Count == 0)
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
        List<NftClaimEntity> claims = await OutstandingQuery(dbCtx)
            .OrderBy(claim => claim.Id)
            .ToListAsync(ct)
            .ConfigureAwait(true);

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

    /// <summary>
    /// The Relics this player sent out from <paramref name="wallet" /> that the hotel's treasury now
    /// holds on the chain — the ones waiting to come home.
    /// </summary>
    /// <remarks>
    /// The database is asked first and the chain second, so a hotel where nobody has ever
    /// transferred anything out costs no RPC call at all. The chain is what decides: a row saying a
    /// Relic left is not evidence it came back.
    /// </remarks>
    private async Task<List<NftAssetEntity>> ReturnedRelicsAsync(
        VortexDbContext dbCtx,
        string wallet,
        CancellationToken ct
    )
    {
        if (!_chain.Enabled || _chain.TreasuryAddress is null || string.IsNullOrWhiteSpace(wallet))
        {
            return [];
        }

        List<NftAssetEntity> sentOut = await dbCtx
            .NftAssets.Where(asset =>
                asset.PlayerEntityId == PlayerId.Value
                && asset.DeletedAt == null
                && asset.WalletAddress == wallet
            )
            .OrderBy(asset => asset.Id)
            .ToListAsync(ct)
            .ConfigureAwait(true);

        return sentOut.Count == 0
            ? []
            : [.. sentOut.Where(await TreasuryHoldsAsync(ct).ConfigureAwait(true))];
    }

    /// <summary>
    /// Burns every returned Relic back into the hotel and answers how many came home.
    /// </summary>
    /// <remarks>
    /// Burned rather than kept, and in that order: the token is destroyed first and the row cleared
    /// only for the ids the chain then agrees are gone. Clearing first would leave the hotel and the
    /// chain both claiming the same Relic, and the next transfer out would revert on an id that
    /// already exists.
    /// </remarks>
    private async Task<int> ClaimReturnedRelicsAsync(VortexDbContext dbCtx, CancellationToken ct)
    {
        if (!_chain.Enabled || _chain.TreasuryAddress is null)
        {
            return 0;
        }

        List<NftAssetEntity> sentOut = await dbCtx
            .NftAssets.Where(asset =>
                asset.PlayerEntityId == PlayerId.Value
                && asset.DeletedAt == null
                && asset.WalletAddress != null
            )
            .OrderBy(asset => asset.Id)
            .ToListAsync(ct)
            .ConfigureAwait(true);

        if (sentOut.Count == 0)
        {
            return 0;
        }

        NftAssetEntity[] home;

        try
        {
            home = [.. sentOut.Where(await TreasuryHoldsAsync(ct).ConfigureAwait(true))];

            foreach (NftAssetEntity asset in home)
            {
                await _chain.BurnRelicAsync(asset.Id, ct).ConfigureAwait(true);
            }

            // Read back, because a transaction hash is a receipt that the node accepted the burn and
            // not that it succeeded. A Relic handed back while its token still exists is one the
            // player could transfer out again onto an id the contract already has.
            Func<NftAssetEntity, bool> stillHeld = await TreasuryHoldsAsync(ct)
                .ConfigureAwait(true);

            home = [.. home.Where(asset => !stillHeld(asset))];
        }
        catch (Exception failure)
        {
            // Nothing has been written at this point, so the claim simply stays outstanding and the
            // player can press the button again once the node is answering.
            _logger.LogError(
                failure,
                "Bringing player {PlayerId}'s Relics back from the treasury failed.",
                PlayerId
            );

            return 0;
        }

        if (home.Length == 0)
        {
            return 0;
        }

        foreach (NftAssetEntity asset in home)
        {
            asset.WalletAddress = null;
        }

        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

        _logger.LogInformation(
            "Player {PlayerId} brought {Count} Relic(s) home from the chain.",
            PlayerId,
            home.Length
        );

        return home.Length;
    }

    /// <summary>
    /// Reads the treasury's Relics once and answers a predicate over them. A test per asset would
    /// otherwise be a JSON-RPC call per asset, where the contract answers the whole wallet in one.
    /// </summary>
    private async Task<Func<NftAssetEntity, bool>> TreasuryHoldsAsync(CancellationToken ct)
    {
        HashSet<BigInteger> held =
        [
            .. await _chain.GetRelicsAsync(_chain.TreasuryAddress!, ct).ConfigureAwait(true),
        ];

        return asset => held.Contains(asset.Id);
    }

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
