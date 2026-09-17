using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Dashboard.API.Api.Progression.Contracts;
using Vortex.Dashboard.API.Infrastructure;
using Vortex.Database.Context;
using Vortex.Primitives.Players.Avatar;

namespace Vortex.Dashboard.API.Api.Progression;

/// <summary>
/// NFT collections, the items that make them up, and the collector scores players have reached.
/// <para>
/// A collection is only completable if the furniture its items name actually exists in the
/// catalogue, so each item is matched against <c>furniture_definitions</c> by classname — an item
/// pointing at nothing is a collection no player can ever finish, and no other surface would show
/// it.
/// </para>
/// </summary>
internal sealed class CollectibleReads(
    IDbContextFactory<VortexDbContext> dbContextFactory,
    DashboardAssetUrls assetUrls
) : DashboardReads(dbContextFactory)
{
    private readonly DashboardAssetUrls _assetUrls = assetUrls;

    /// <summary>
    /// Whether the client will treat this furniture as a collectible at all. It decides from the
    /// classname alone — <c>GroupItem.isNft()</c> is <c>className.indexOf("nft_") == 0</c> — and
    /// uses it both to keep such items out of the ordinary Furni list and to fill the Collectibles
    /// category. A shop offer or a relic naming anything else still works, but the buyer receives
    /// what the client files as plain furniture.
    /// </summary>
    private static bool IsCollectibleClassname(string productCode) =>
        productCode.StartsWith("nft_", StringComparison.Ordinal);

    public Task<CollectiblesOverview> CollectiblesAsync(CancellationToken ct) =>
        QueryAsync<CollectiblesOverview>(
            async db =>
            {
                var collections = await db
                    .NftCollections.AsNoTracking()
                    .OrderBy(c => c.Id)
                    .Select(c => new
                    {
                        c.Id,
                        c.CollectionCode,
                        c.Name,
                        c.BoostScore,
                        c.ReleasedAt,
                        c.SnapshotAt,
                        c.Status,
                        c.RewardProductCode,
                        c.BonusProductCode,
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                var items = await db
                    .NftCollectionItems.AsNoTracking()
                    .OrderBy(i => i.NftCollectionEntityId)
                    .ThenBy(i => i.SortOrder)
                    .Select(i => new
                    {
                        i.Id,
                        collectionId = i.NftCollectionEntityId,
                        i.ProductCode,
                        i.ItemTypeId,
                        i.ProductTypeId,
                        i.Score,
                        i.Rarity,
                        i.SortOrder,
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                List<string> productCodes = items.Select(i => i.ProductCode).Distinct().ToList();

                // Matching by classname is what the collection's completion check has to do too: the
                // rows carry a product code, not a definition id.
                HashSet<string> knownFurniture = new(
                    await db
                        .FurnitureDefinitions.AsNoTracking()
                        .Where(f => productCodes.Contains(f.Name))
                        .Select(f => f.Name)
                        .ToListAsync(ct)
                        .ConfigureAwait(false),
                    StringComparer.Ordinal
                );

                List<CollectionRow> collectionItems = collections
                    .Select(c =>
                    {
                        var owned = items.Where(i => i.collectionId == c.Id).ToList();
                        int unresolved = owned.Count(i => !knownFurniture.Contains(i.ProductCode));

                        return new CollectionRow(
                            c.Id,
                            c.CollectionCode,
                            c.Name,
                            c.BoostScore,
                            c.ReleasedAt,
                            c.SnapshotAt,
                            c.Status,
                            c.RewardProductCode,
                            c.BonusProductCode,
                            owned.Count,
                            owned.Sum(i => i.Score),
                            unresolved,
                            owned.Count > 0 && unresolved == 0,
                            owned
                                .Select(i => new CollectionItemRow(
                                    i.Id,
                                    i.ProductCode,
                                    i.ItemTypeId,
                                    i.ProductTypeId,
                                    i.Score,
                                    i.Rarity,
                                    i.SortOrder,
                                    knownFurniture.Contains(i.ProductCode),
                                    _assetUrls.FurniIcon(i.ProductCode)
                                ))
                                .ToList()
                        );
                    })
                    .ToList();

                // The shop is checked the same way a collection item is: an offer naming furniture
                // that does not exist would take the buyer's emeralds and hand over nothing, so the
                // grain refuses the sale — and this is the only place that would say so beforehand.
                var offerRows = await db
                    .NftStoreOffers.AsNoTracking()
                    .OrderBy(o => o.SortOrder)
                    .ThenBy(o => o.Id)
                    .Select(o => new
                    {
                        o.Id,
                        o.ProductCode,
                        o.EmeraldPrice,
                        o.IsFeatured,
                        o.IsLimited,
                        o.MintLimit,
                        o.SoldCount,
                        o.ItemTypeId,
                        o.ProductTypeId,
                        o.Score,
                        o.Rarity,
                        o.Enabled,
                        o.SortOrder,
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                List<string> offerCodes = offerRows.Select(o => o.ProductCode).Distinct().ToList();

                HashSet<string> knownOfferFurniture = new(
                    await db
                        .FurnitureDefinitions.AsNoTracking()
                        .Where(f => offerCodes.Contains(f.Name))
                        .Select(f => f.Name)
                        .ToListAsync(ct)
                        .ConfigureAwait(false),
                    StringComparer.Ordinal
                );

                List<NftStoreOfferRow> storeOffers = offerRows
                    .Select(o => new NftStoreOfferRow(
                        o.Id,
                        o.ProductCode,
                        o.EmeraldPrice,
                        o.IsFeatured,
                        o.IsLimited,
                        o.MintLimit,
                        o.SoldCount,
                        o.ItemTypeId,
                        o.ProductTypeId,
                        o.Score,
                        o.Rarity,
                        o.Enabled,
                        o.SortOrder,
                        knownOfferFurniture.Contains(o.ProductCode),
                        o.MintLimit > 0 && o.SoldCount >= o.MintLimit,
                        // The client decides what counts as a collectible purely from the
                        // classname: GroupItem.isNft() is className.indexOf("nft_") == 0. Anything
                        // else is hidden from the inventory's Collectibles category and listed as
                        // ordinary furniture, however it was bought.
                        IsCollectibleClassname(o.ProductCode),
                        _assetUrls.FurniIcon(o.ProductCode)
                    ))
                    .ToList();

                // The Relics waiting to be collected. Only outstanding ones are listed: a claim the
                // player has already taken in full is history, not a reward.
                var claimRows = await db
                    .NftClaims.AsNoTracking()
                    .Where(c => c.ClaimedAmount < c.ClaimLimit)
                    .OrderBy(c => c.Id)
                    .Select(c => new
                    {
                        c.Id,
                        c.PlayerEntityId,
                        c.ProductCode,
                        c.SetId,
                        c.Collection,
                        c.ClaimLimit,
                        c.ClaimedAmount,
                        c.ValidFrom,
                        c.ValidTo,
                        c.MinCollectorLevel,
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                Dictionary<int, string> claimNames = await db.PlayerNamesAsync(
                        DisplayNameQueries.NormalizeIds(
                            claimRows.Select(c => (int?)c.PlayerEntityId)
                        ),
                        ct
                    )
                    .ConfigureAwait(false);

                List<NftClaimRow> claims = claimRows
                    .Select(c => new NftClaimRow(
                        c.Id,
                        c.PlayerEntityId,
                        DisplayNameQueries.ResolvePlayerName(claimNames, c.PlayerEntityId),
                        c.ProductCode,
                        c.SetId,
                        c.Collection,
                        c.ClaimLimit,
                        c.ClaimedAmount,
                        c.ClaimLimit - c.ClaimedAmount,
                        c.ValidFrom,
                        c.ValidTo,
                        c.MinCollectorLevel,
                        IsCollectibleClassname(c.ProductCode),
                        _assetUrls.FurniIcon(c.ProductCode)
                    ))
                    .ToList();

                // Minting: what may be converted into a Relic, and what stamps cost. A type naming
                // furniture that does not exist is dropped by the grain rather than listed — the
                // client would count the player's copies by a sprite id it has no definition for —
                // so the unresolved flag here is the only warning an admin gets.
                var mintableRows = await db
                    .NftMintableItemTypes.AsNoTracking()
                    .OrderBy(t => t.SortOrder)
                    .ThenBy(t => t.Id)
                    .Select(t => new
                    {
                        t.Id,
                        t.ProductCode,
                        t.StampPrice,
                        t.StartsAt,
                        t.EndsAt,
                        t.RegionLocked,
                        t.LimitedEdition,
                        t.EditionSize,
                        t.Enabled,
                        t.SortOrder,
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                List<string> mintableCodes = mintableRows
                    .Select(t => t.ProductCode)
                    .Distinct()
                    .ToList();

                HashSet<string> knownMintableFurniture = new(
                    await db
                        .FurnitureDefinitions.AsNoTracking()
                        .Where(f => mintableCodes.Contains(f.Name))
                        .Select(f => f.Name)
                        .ToListAsync(ct)
                        .ConfigureAwait(false),
                    StringComparer.Ordinal
                );

                Dictionary<string, int> mintedByCode = await db
                    .NftAssets.AsNoTracking()
                    .Where(a => a.DeletedAt == null && mintableCodes.Contains(a.ProductCode))
                    .GroupBy(a => a.ProductCode)
                    .Select(g => new { ProductCode = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(r => r.ProductCode, r => r.Count, ct)
                    .ConfigureAwait(false);

                DateTime now = DateTime.UtcNow;

                List<MintableTypeRow> mintableTypes = mintableRows
                    .Select(t => new MintableTypeRow(
                        t.Id,
                        t.ProductCode,
                        t.StampPrice,
                        t.StartsAt,
                        t.EndsAt,
                        t.RegionLocked,
                        t.LimitedEdition,
                        t.EditionSize,
                        t.Enabled,
                        t.SortOrder,
                        // How much of a limited edition is gone. Counted from the Relics that
                        // exist, which is also what the mint checks against.
                        mintedByCode.GetValueOrDefault(t.ProductCode),
                        t.EditionSize > 0
                            && mintedByCode.GetValueOrDefault(t.ProductCode) >= t.EditionSize,
                        knownMintableFurniture.Contains(t.ProductCode),
                        // What the player sees: a type is only convertible while its window is
                        // open, and the client gives no reason for a closed one.
                        t.Enabled
                            && t.StartsAt <= now
                            && t.EndsAt > now,
                        t.EndsAt <= now,
                        IsCollectibleClassname(t.ProductCode),
                        _assetUrls.FurniIcon(t.ProductCode)
                    ))
                    .ToList();

                List<MintTokenOfferRow> tokenOffers = await db
                    .NftMintTokenOffers.AsNoTracking()
                    .OrderBy(o => o.SortOrder)
                    .ThenBy(o => o.Id)
                    .Select(o => new MintTokenOfferRow(
                        o.Id,
                        o.ProductCode,
                        o.SilverPrice,
                        o.AmountTokens,
                        o.Enabled,
                        o.SortOrder
                    ))
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                var assetRows = await db
                    .NftAssets.AsNoTracking()
                    .Where(a => a.DeletedAt == null)
                    .OrderByDescending(a => a.Id)
                    .Take(50)
                    .Select(a => new
                    {
                        a.Id,
                        a.PlayerEntityId,
                        a.ProductCode,
                        a.StampCost,
                        a.SerialNumber,
                        a.EditionSize,
                        a.CreatedAt,
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                List<int> assetIds = assetRows.Select(a => a.Id).ToList();

                // Where each of them came from. This is the part of a chain worth keeping: an
                // admin can answer "who had this before" without one.
                var ledgerRows = await db
                    .NftAssetLedger.AsNoTracking()
                    .Where(l => assetIds.Contains(l.NftAssetEntityId))
                    .OrderByDescending(l => l.Id)
                    .Select(l => new
                    {
                        l.Id,
                        assetId = l.NftAssetEntityId,
                        l.FromPlayerEntityId,
                        l.ToPlayerEntityId,
                        l.Reason,
                        l.CreatedAt,
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                Dictionary<int, string> minterNames = await db.PlayerNamesAsync(
                        DisplayNameQueries.NormalizeIds(
                            assetRows
                                .Select(a => (int?)a.PlayerEntityId)
                                .Concat(ledgerRows.Select(l => l.FromPlayerEntityId))
                                .Concat(ledgerRows.Select(l => (int?)l.ToPlayerEntityId))
                        ),
                        ct
                    )
                    .ConfigureAwait(false);

                List<NftAssetRow> assets = assetRows
                    .Select(a => new NftAssetRow(
                        a.Id,
                        a.PlayerEntityId,
                        DisplayNameQueries.ResolvePlayerName(minterNames, a.PlayerEntityId),
                        a.ProductCode,
                        a.StampCost,
                        a.SerialNumber,
                        a.EditionSize,
                        a.CreatedAt,
                        _assetUrls.FurniIcon(a.ProductCode),
                        ledgerRows
                            .Where(l => l.assetId == a.Id)
                            .Select(l => new NftAssetTransfer(
                                l.Id,
                                l.FromPlayerEntityId is null
                                    ? null
                                    : DisplayNameQueries.ResolvePlayerName(
                                        minterNames,
                                        l.FromPlayerEntityId.Value
                                    ),
                                DisplayNameQueries.ResolvePlayerName(
                                    minterNames,
                                    l.ToPlayerEntityId
                                ),
                                l.Reason,
                                l.CreatedAt
                            ))
                            .ToList()
                    ))
                    .ToList();

                int mintedTotal = await db
                    .NftAssets.AsNoTracking()
                    .CountAsync(a => a.DeletedAt == null, ct)
                    .ConfigureAwait(false);

                int stampsHeld = await db
                    .PlayerMintTokens.AsNoTracking()
                    .Where(row => row.DeletedAt == null)
                    .SumAsync(row => row.Balance, ct)
                    .ConfigureAwait(false);

                var collectorRows = await db
                    .PlayerCollectorStats.AsNoTracking()
                    .OrderByDescending(s => s.HighestScore)
                    .Take(15)
                    .Select(s => new { s.PlayerEntityId, s.HighestScore })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                Dictionary<int, string> names = await db.PlayerNamesAsync(
                        DisplayNameQueries.NormalizeIds(
                            collectorRows.Select(c => (int?)c.PlayerEntityId)
                        ),
                        ct
                    )
                    .ConfigureAwait(false);

                int trackedPlayers = await db
                    .PlayerCollectorStats.AsNoTracking()
                    .CountAsync(ct)
                    .ConfigureAwait(false);

                // Wearable avatars, and who holds a copy. Both halves are listed together because
                // the model on its own says nothing useful — an avatar nobody was given is an
                // avatar that does not exist as far as the hotel is concerned.
                var avatarRows = await db
                    .NftAvatars.AsNoTracking()
                    .OrderBy(a => a.SortOrder)
                    .ThenBy(a => a.Id)
                    .Select(a => new
                    {
                        a.Id,
                        a.AvatarCode,
                        a.Name,
                        a.Figure,
                        a.Gender,
                        a.ContractKey,
                        a.EditionSize,
                        a.Enabled,
                        a.SortOrder,
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                var copyRows = await db
                    .PlayerNftAvatars.AsNoTracking()
                    .Where(owned => owned.DeletedAt == null)
                    .OrderBy(owned => owned.NftAvatarEntityId)
                    .ThenBy(owned => owned.SerialNumber)
                    .Select(owned => new
                    {
                        owned.Id,
                        avatarId = owned.NftAvatarEntityId,
                        owned.PlayerEntityId,
                        owned.SerialNumber,
                        owned.GrantNote,
                        owned.CreatedAt,
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                Dictionary<int, string> holderNames = await db.PlayerNamesAsync(
                        DisplayNameQueries.NormalizeIds(
                            copyRows.Select(c => (int?)c.PlayerEntityId)
                        ),
                        ct
                    )
                    .ConfigureAwait(false);

                HashSet<int> wornCopyIds = new(
                    await db
                        .PlayerNftOutfits.AsNoTracking()
                        .Where(row => row.DeletedAt == null)
                        .Select(row => row.PlayerNftAvatarEntityId)
                        .ToListAsync(ct)
                        .ConfigureAwait(false)
                );

                List<NftAvatarRow> nftAvatars = avatarRows
                    .Select(a => new NftAvatarRow(
                        a.Id,
                        a.AvatarCode,
                        a.Name,
                        a.Figure,
                        a.Gender,
                        a.ContractKey,
                        a.EditionSize,
                        a.Enabled,
                        a.SortOrder,
                        copyRows.Count(c => c.avatarId == a.Id),
                        a.EditionSize > 0
                            && copyRows.Count(c => c.avatarId == a.Id) >= a.EditionSize,
                        // The caption the player reads under the tile is built from this string,
                        // and the client has no branch for one it does not know: it prints the
                        // literal word "null" instead of a collection name.
                        NftAvatarCollection.IsKnown(a.ContractKey),
                        _assetUrls.AvatarImage(a.Figure),
                        copyRows
                            .Where(c => c.avatarId == a.Id)
                            .Select(c => new NftAvatarHolder(
                                c.Id,
                                c.PlayerEntityId,
                                DisplayNameQueries.ResolvePlayerName(holderNames, c.PlayerEntityId),
                                c.SerialNumber,
                                c.GrantNote,
                                c.CreatedAt,
                                wornCopyIds.Contains(c.Id)
                            ))
                            .ToList()
                    ))
                    .ToList();

                return new CollectiblesOverview(
                    new CollectiblesTotals(
                        collectionItems.Count,
                        items.Count,
                        collectionItems.Sum(c => c.UnresolvedItems),
                        collectionItems.Count(c => c.Completable),
                        trackedPlayers,
                        storeOffers.Count,
                        storeOffers.Count(o => o.Enabled && !o.SoldOut && o.Resolved),
                        mintableTypes.Count,
                        mintableTypes.Count(t => t.Open && t.Resolved),
                        mintedTotal,
                        stampsHeld,
                        nftAvatars.Count,
                        nftAvatars.Sum(a => a.GrantedCount)
                    ),
                    collectionItems,
                    nftAvatars,
                    storeOffers,
                    mintableTypes,
                    tokenOffers,
                    assets,
                    claims,
                    collectorRows
                        .Select(c => new CollectorScore(
                            c.PlayerEntityId,
                            DisplayNameQueries.ResolvePlayerName(names, c.PlayerEntityId),
                            c.HighestScore
                        ))
                        .ToList()
                );
            },
            ct
        );
}
