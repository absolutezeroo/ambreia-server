using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Entities.Collectibles;
using Vortex.Primitives.Collectibles;
using Vortex.Primitives.Collectibles.Grains;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Players;

namespace Vortex.Collectibles.Grains;

/// <summary>
/// The hotel's collections, and where a player stands in them.
/// <para>
/// The definitions are cached for the lifetime of the kept-alive singleton, the way
/// <see cref="MysteryBoxManagerGrain"/> caches its own: they change when an admin edits them, not
/// while anybody is playing. What a player <em>owns</em> is counted per request, because it changes
/// with every purchase, trade and sale.
/// </para>
/// <para>
/// A collection needs no blockchain — it is a list of classnames and a prize for owning them — which
/// is why this half of the collectibles interface works on a hotel that will never mint anything.
/// </para>
/// </summary>
[KeepAlive]
internal sealed class NftCollectionsGrain(
    IDbContextFactory<VortexDbContext> dbCtxFactory,
    ILogger<NftCollectionsGrain> logger
) : Grain, INftCollectionsGrain
{
    private readonly IDbContextFactory<VortexDbContext> _dbCtxFactory = dbCtxFactory;
    private readonly ILogger<NftCollectionsGrain> _logger = logger;

    private ImmutableArray<CachedCollection> _collections = [];
    private bool _loaded;

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await LoadAsync(ct).ConfigureAwait(true);
        await base.OnActivateAsync(ct).ConfigureAwait(true);
    }

    public async Task<ImmutableArray<NftCollectionSnapshot>> GetCollectionsForPlayerAsync(
        PlayerId playerId,
        CancellationToken ct
    )
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(true);

        if (_collections.Length == 0)
        {
            return [];
        }

        Dictionary<string, int> owned = await CountOwnedAsync(playerId, ct).ConfigureAwait(true);

        return [.. _collections.Select(collection => ToSnapshot(collection, owned))];
    }

    public async Task<CollectorScoreSnapshot> GetCollectorScoreAsync(
        PlayerId playerId,
        CancellationToken ct
    )
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(true);

        Dictionary<string, int> owned = await CountOwnedAsync(playerId, ct).ConfigureAwait(true);

        int score = 0;
        int completed = 0;

        foreach (CachedCollection collection in _collections)
        {
            int collectionScore = ScoreOf(collection, owned);

            score += collectionScore;

            if (IsComplete(collection, owned))
            {
                completed++;
            }
        }

        int highest = await ReadAndRaiseHighestScoreAsync(playerId, score, ct).ConfigureAwait(true);

        return new CollectorScoreSnapshot
        {
            Score = score,
            HighestScore = highest,
            Level = completed,
        };
    }

    public Task ReloadAsync(CancellationToken ct) => LoadAsync(ct);

    /// <summary>
    /// How many of each collected classname the player holds, across their whole account rather
    /// than one room: a collection is about owning the thing, not about where it is standing.
    /// <para>
    /// Relics count too. Converting a piece of furniture destroys it, so counting only furniture
    /// would mean minting a collectible drops the score of the collection it belongs to — the
    /// Collectors Guild punishing collecting. A Relic is the same thing in another form, and is
    /// counted as one of it.
    /// </para>
    /// </summary>
    private async Task<Dictionary<string, int>> CountOwnedAsync(
        PlayerId playerId,
        CancellationToken ct
    )
    {
        HashSet<string> wanted =
        [
            .. _collections.SelectMany(collection =>
                collection.Items.Select(item => item.ProductCode)
            ),
        ];

        if (wanted.Count == 0)
        {
            return [];
        }

        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(true);

        // Grouped in the database rather than pulled back row by row: a collector's account can
        // hold thousands of pieces of furniture and only the counts are wanted.
        List<OwnedCount> counts = await dbCtx
            .Furnitures.AsNoTracking()
            .Where(furni =>
                furni.PlayerEntityId == playerId.Value
                && furni.DeletedAt == null
                && furni.FurnitureDefinitionEntity != null
                && wanted.Contains(furni.FurnitureDefinitionEntity.Name)
            )
            .GroupBy(furni => furni.FurnitureDefinitionEntity!.Name)
            .Select(group => new OwnedCount(group.Key, group.Count()))
            .ToListAsync(ct)
            .ConfigureAwait(true);

        // A Relic out on the chain counts for nobody here. That is a decision and not an oversight:
        // the alternative is to keep crediting `player_id`, which stops being an owner the moment
        // the Relic leaves — a player who sells theirs to a stranger would keep the collection score
        // for it forever, and the hotel has no way to learn otherwise. Counting only what is in the
        // hotel costs the seller a score they no longer deserve, and it is the same rule the
        // inventory and the trade table use.
        List<OwnedCount> relics = await dbCtx
            .NftAssets.AsNoTracking()
            .Where(asset =>
                asset.PlayerEntityId == playerId.Value
                && asset.DeletedAt == null
                && asset.WalletAddress == null
                && wanted.Contains(asset.ProductCode)
            )
            .GroupBy(asset => asset.ProductCode)
            .Select(group => new OwnedCount(group.Key, group.Count()))
            .ToListAsync(ct)
            .ConfigureAwait(true);

        Dictionary<string, int> owned = counts.ToDictionary(
            count => count.ProductCode,
            count => count.Amount,
            StringComparer.OrdinalIgnoreCase
        );

        foreach (OwnedCount relic in relics)
        {
            owned[relic.ProductCode] = owned.GetValueOrDefault(relic.ProductCode) + relic.Amount;
        }

        return owned;
    }

    /// <summary>
    /// Reads the player's best score, raising it when the live one has just beaten it. Written on
    /// read because that is the only moment the two are compared, and a score nobody looked at
    /// cannot have been a personal best worth showing.
    /// </summary>
    private async Task<int> ReadAndRaiseHighestScoreAsync(
        PlayerId playerId,
        int score,
        CancellationToken ct
    )
    {
        try
        {
            await using VortexDbContext dbCtx = await _dbCtxFactory
                .CreateDbContextAsync(ct)
                .ConfigureAwait(true);

            PlayerCollectorStatsEntity? stats = await dbCtx
                .PlayerCollectorStats.SingleOrDefaultAsync(
                    row => row.PlayerEntityId == playerId.Value && row.DeletedAt == null,
                    ct
                )
                .ConfigureAwait(true);

            if (stats is null)
            {
                dbCtx.PlayerCollectorStats.Add(
                    new PlayerCollectorStatsEntity
                    {
                        PlayerEntityId = playerId.Value,
                        HighestScore = score,
                    }
                );

                await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

                return score;
            }

            if (score <= stats.HighestScore)
            {
                return stats.HighestScore;
            }

            stats.HighestScore = score;

            await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

            return score;
        }
        catch (Exception ex)
        {
            // A best score that could not be written is not worth failing the whole panel over.
            _logger.LogWarning(
                ex,
                "Failed to record a collector best score for player {PlayerId}.",
                playerId
            );

            return score;
        }
    }

    private static NftCollectionSnapshot ToSnapshot(
        CachedCollection collection,
        Dictionary<string, int> owned
    )
    {
        ImmutableArray<CollectibleProductItemSnapshot> items =
        [
            .. collection.Items.Select(item =>
                item.Snapshot with
                {
                    Amount = owned.GetValueOrDefault(item.ProductCode, 0),
                }
            ),
        ];

        return new NftCollectionSnapshot
        {
            CollectionId = collection.CollectionCode,
            CollectionName = collection.Name,
            Items = items,
            CollectionScore = ScoreOf(collection, owned),
            CollectionTotalScore = collection.TotalScore,
            CollectionBoostScore = collection.BoostScore,
            BonusItem = collection.BonusItem,
            RewardItem = collection.RewardItem,
            ReleasedTimeMs = collection.ReleasedTimeMs,
            SnapshotTimeMs = collection.SnapshotTimeMs,
            Status = collection.Status,
            BonusItemClaim = collection.BonusItem is null ? null : BuildClaim(collection, "bonus"),
            RewardItemClaim = collection.RewardItem is null
                ? null
                : BuildClaim(collection, "reward"),
        };
    }

    /// <summary>
    /// The prize's claim. It reads as not-claimable even on a finished collection, because nothing
    /// hands the prize over yet: saying claimable would light up a button that does nothing, which
    /// is worse for a player than a prize plainly out of reach.
    /// </summary>
    private static CollectibleItemClaimSnapshot BuildClaim(
        CachedCollection collection,
        string kind
    ) =>
        new()
        {
            ClaimId = $"{collection.CollectionCode}:{kind}",
            ClaimedAmount = 0,
            ClaimLimit = 1,
            Status = CollectibleClaimStatus.NotClaimable,
        };

    /// <summary>
    /// What the player has scored here: each item they own at all, plus the boost once the set is
    /// complete. Owning six of one thing is not six times the collector, which is why the count
    /// does not multiply the score.
    /// </summary>
    private static int ScoreOf(CachedCollection collection, Dictionary<string, int> owned)
    {
        int score = collection
            .Items.Where(item => owned.GetValueOrDefault(item.ProductCode, 0) > 0)
            .Sum(item => item.Snapshot.Score);

        return IsComplete(collection, owned) ? score + collection.BoostScore : score;
    }

    private static bool IsComplete(CachedCollection collection, Dictionary<string, int> owned) =>
        collection.Items.Length > 0
        && collection.Items.All(item => owned.GetValueOrDefault(item.ProductCode, 0) > 0);

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (!_loaded)
        {
            await LoadAsync(ct).ConfigureAwait(true);
        }
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        try
        {
            await using VortexDbContext dbCtx = await _dbCtxFactory
                .CreateDbContextAsync(ct)
                .ConfigureAwait(true);

            // Drafts and archived collections are filtered here rather than at the send, so nothing
            // downstream has to remember the rule. The client never reads the status it is given,
            // so hiding an unpublished collection is only ever going to happen on this side.
            NftCollectionEntity[] collections = await dbCtx
                .NftCollections.AsNoTracking()
                .Where(collection =>
                    collection.DeletedAt == null && collection.Status == NftCollectionStatus.Visible
                )
                .Include(collection => collection.Items!.Where(item => item.DeletedAt == null))
                .OrderBy(collection => collection.Id)
                .ToArrayAsync(ct)
                .ConfigureAwait(true);

            // The client draws every collectible — the items and both prizes — by looking a sprite
            // id up in its own furniture tables, so the picture comes from the definition rather
            // than from anything stored on the collection. Gathered in one query for the whole set.
            IEnumerable<string?> codes = collections.SelectMany(collection =>
                (collection.Items ?? [])
                    .Select(item => item.ProductCode)
                    .Concat([collection.BonusProductCode, collection.RewardProductCode])
            );

            Dictionary<string, FurnitureIdentity> definitions = await FurnitureDefinitionLookup
                .ResolveByClassNameAsync(
                    dbCtx,
                    codes,
                    definition => new FurnitureIdentity(
                        definition.SpriteId,
                        definition.ProductType
                    ),
                    ct,
                    _logger
                )
                .ConfigureAwait(true);

            _collections = [.. collections.Select(entity => ToCached(entity, definitions))];
            _loaded = true;

            _logger.LogInformation(
                "Loaded {CollectionCount} collectible collections.",
                _collections.Length
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load collectible collections.");

            _collections = [];
            _loaded = false;
        }
    }

    private static CachedCollection ToCached(
        NftCollectionEntity entity,
        Dictionary<string, FurnitureIdentity> definitions
    )
    {
        CachedItem[] items =
        [
            .. (entity.Items ?? [])
                .OrderBy(item => item.SortOrder)
                .ThenBy(item => item.Id)
                .Select(item => new CachedItem(
                    item.ProductCode,
                    ToProductItem(item.ProductCode, definitions) with
                    {
                        Score = item.Score,
                        Rarity = item.Rarity,
                    }
                )),
        ];

        return new CachedCollection(
            entity.CollectionCode,
            entity.Name,
            [.. items],
            items.Sum(item => item.Snapshot.Score) + entity.BoostScore,
            entity.BoostScore,
            ToPrizeItem(entity.BonusProductCode, definitions),
            ToPrizeItem(entity.RewardProductCode, definitions),
            ToUnixMs(entity.ReleasedAt),
            ToUnixMs(entity.SnapshotAt),
            entity.Status
        );
    }

    /// <summary>A prize is drawn as an item like any other, so it is shaped like one.</summary>
    private static CollectibleProductItemSnapshot? ToPrizeItem(
        string? productCode,
        Dictionary<string, FurnitureIdentity> definitions
    ) => string.IsNullOrWhiteSpace(productCode) ? null : ToProductItem(productCode, definitions);

    /// <summary>
    /// One collectible as the client needs it. The picture is decided by the sprite id and the
    /// table to look it up in, both taken from the furniture definition — see
    /// <see cref="CollectibleProductIdentity"/> for why neither may be stored or typed.
    /// </summary>
    private static CollectibleProductItemSnapshot ToProductItem(
        string productCode,
        Dictionary<string, FurnitureIdentity> definitions
    )
    {
        definitions.TryGetValue(productCode, out FurnitureIdentity definition);

        return new CollectibleProductItemSnapshot
        {
            ProductTypeId = CollectibleProductIdentity.ForFurniture(definition.ProductType),
            ItemTypeId = CollectibleProductIdentity.ItemTypeId(definition.SpriteId),
            Score = 0,
            ProductCode = productCode,
        };
    }

    /// <summary>What a classname resolves to in the catalogue: the two things the client needs to
    /// draw it.</summary>
    private readonly record struct FurnitureIdentity(int SpriteId, ProductType ProductType);

    private static long ToUnixMs(DateTime? value) =>
        value is null
            ? 0
            : new DateTimeOffset(
                DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
            ).ToUnixTimeMilliseconds();

    private sealed record CachedItem(string ProductCode, CollectibleProductItemSnapshot Snapshot);

    private sealed record CachedCollection(
        string CollectionCode,
        string Name,
        ImmutableArray<CachedItem> Items,
        int TotalScore,
        int BoostScore,
        CollectibleProductItemSnapshot? BonusItem,
        CollectibleProductItemSnapshot? RewardItem,
        long ReleasedTimeMs,
        long SnapshotTimeMs,
        int Status
    );

    private sealed record OwnedCount(string ProductCode, int Amount);
}
