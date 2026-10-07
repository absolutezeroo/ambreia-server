using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vortex.Database.Context;
using Vortex.Database.Entities.Furniture;
using Vortex.Furniture.Configuration;
using Vortex.Primitives.Furniture;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Furniture.Snapshots;
using Vortex.Primitives.Hosting;
using Vortex.Primitives.Observability;

namespace Vortex.Furniture.Providers;

public sealed class FurnitureDefinitionProvider(
    IOptions<FurnitureConfig> config,
    IDbContextFactory<VortexDbContext> dbCtxFactory,
    IVortexMetrics metrics,
    ILogger<IFurnitureDefinitionProvider> logger
) : IFurnitureDefinitionProvider, IReferenceDataProvider
{
    private readonly FurnitureConfig _config = config.Value;
    private readonly IDbContextFactory<VortexDbContext> _dbCtxFactory = dbCtxFactory;
    private readonly IVortexMetrics _metrics = metrics;
    private readonly ILogger<IFurnitureDefinitionProvider> _logger = logger;

    // Stage 0: loaded with the first batch, since it depends on no other provider.
    public int LoadStage => 0;

    /// <summary>
    /// Both indexes and the version they were built at, published as one object.
    /// </summary>
    /// <remarks>
    /// They used to be two fields assigned one after the other, so a reload had a window in
    /// which a reader could see the new definitions by id and the old ones by name. Small window,
    /// rare event, and a bug that would have been extremely hard to recognise from its symptoms —
    /// which is exactly the argument for closing it while it costs one object.
    /// </remarks>
    private sealed record FurnitureDefinitionSet
    {
        public required ImmutableDictionary<int, FurnitureDefinitionSnapshot> ById { get; init; }

        // Classnames are not unique in this catalogue -- tile_stackmagic and roomdimmer each appear
        // twice -- so the index keeps the first of a duplicate rather than throwing the whole reload.
        public required ImmutableDictionary<
            string,
            FurnitureDefinitionSnapshot
        > ByName { get; init; }

        /// <summary>Increments once per successful reload, and is exported as a metric so an
        /// operator can tell whether the definitions they just edited are the ones being served.</summary>
        public required int Version { get; init; }

        public static readonly FurnitureDefinitionSet Empty = new()
        {
            ById = ImmutableDictionary<int, FurnitureDefinitionSnapshot>.Empty,
            ByName = ImmutableDictionary<string, FurnitureDefinitionSnapshot>.Empty,
            Version = 0,
        };
    }

    private FurnitureDefinitionSet _definitions = FurnitureDefinitionSet.Empty;

    public FurnitureDefinitionSnapshot? TryGetDefinition(int id) =>
        _definitions.ById.TryGetValue(id, out FurnitureDefinitionSnapshot? definition)
            ? definition
            : null;

    public FurnitureDefinitionSnapshot? TryGetDefinitionByName(string name) =>
        _definitions.ByName.TryGetValue(name, out FurnitureDefinitionSnapshot? definition)
            ? definition
            : null;

    public async Task ReloadAsync(CancellationToken ct = default)
    {
        VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        try
        {
            List<FurnitureDefinitionEntity> entities = await dbCtx
                .FurnitureDefinitions.AsNoTracking()
                .ToListAsync(ct)
                .ConfigureAwait(false);

            List<FurnitureDefinitionSnapshot> defs = entities
                .Select(x => new FurnitureDefinitionSnapshot
                {
                    Id = x.Id,
                    SpriteId = x.SpriteId,
                    Name = x.Name,
                    ProductType = x.ProductType,
                    FurniCategory = x.FurniCategory,
                    LogicName = x.Logic,
                    TotalStates = x.TotalStates,
                    Width = x.Width,
                    Length = x.Length,
                    StackHeight = Math.Round(Math.Max(_config.MinimumZValue, x.StackHeight), 2),
                    CanStack = x.CanStack,
                    CanWalk = x.CanWalk,
                    CanSit = x.CanSit,
                    CanLay = x.CanLay,
                    CanRecycle = x.CanRecycle,
                    CanTrade = x.CanTrade,
                    CanGroup = x.CanGroup,
                    CanSell = x.CanSell,
                    UsagePolicy = x.UsagePolicy,
                    ExtraData = x.ExtraData,
                    StuffDataType = x.StuffDataType,
                    VendingIds = VendingIdParser.Parse(x.VendingIds),
                })
                .ToList();

            FurnitureDefinitionSet published = new()
            {
                ById = defs.ToImmutableDictionary(p => p.Id),
                ByName = defs.DistinctBy(p => p.Name).ToImmutableDictionary(p => p.Name),
                Version = _definitions.Version + 1,
            };

            // One write. A reader sees the set that was there or the set that replaced it, never a
            // half-swapped pair of indexes.
            Volatile.Write(ref _definitions, published);

            _metrics.ReferenceDataPublished(nameof(FurnitureDefinitionProvider), published.Version);

            _logger.LogInformation(
                "Loaded {TotalDefCount} furniture definitions (version {Version})",
                published.ById.Count,
                published.Version
            );
        }
        finally
        {
            await dbCtx.DisposeAsync().ConfigureAwait(false);
        }
    }
}
