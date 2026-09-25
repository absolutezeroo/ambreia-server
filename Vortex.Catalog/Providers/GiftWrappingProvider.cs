using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Database.Entities.Catalog;
using Vortex.Primitives.Catalog.Enums;
using Vortex.Primitives.Catalog.Providers;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Furniture.Snapshots;
using Vortex.Primitives.Hosting;
using Vortex.Primitives.Snapshots.Catalog;

namespace Vortex.Catalog.Providers;

public sealed class GiftWrappingProvider(
    IDbContextFactory<VortexDbContext> dbCtxFactory,
    ILogger<IGiftWrappingProvider> logger,
    IFurnitureDefinitionProvider furnitureProvider
) : IGiftWrappingProvider, IReferenceDataProvider
{
    private readonly IDbContextFactory<VortexDbContext> _dbCtxFactory = dbCtxFactory;
    private readonly ILogger<IGiftWrappingProvider> _logger = logger;
    private readonly IFurnitureDefinitionProvider _furnitureProvider = furnitureProvider;

    // Stage 1: parcel rows resolve through furniture definitions.
    public int LoadStage => 1;

    private Dictionary<int, FurnitureDefinitionSnapshot> _presents = [];

    public GiftWrappingConfigurationSnapshot Configuration { get; private set; } =
        new(false, 0, [], [], [], []);

    public FurnitureDefinitionSnapshot? ResolvePresent(int stuffTypeId) =>
        _presents.GetValueOrDefault(stuffTypeId);

    public async Task ReloadAsync(CancellationToken ct)
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        List<CatalogGiftWrappingEntity> rows = await dbCtx
            .CatalogGiftWrappings.AsNoTracking()
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        Dictionary<int, FurnitureDefinitionSnapshot> presents = [];
        List<int> stuffTypes = [];
        List<int> defaultStuffTypes = [];

        foreach (
            CatalogGiftWrappingEntity row in rows.Where(r => r.Kind == GiftWrappingKind.Parcel)
        )
        {
            FurnitureDefinitionSnapshot? definition = row.FurnitureName is null
                ? null
                : _furnitureProvider.TryGetDefinitionByName(row.FurnitureName);

            // Dropped rather than sent as a hole: the client would ask its room engine to draw it
            // and get nothing back.
            if (definition is null)
            {
                _logger.LogWarning(
                    "Gift wrapping row {Id} names no furniture ({Name}); skipped",
                    row.Id,
                    row.FurnitureName
                );
                continue;
            }

            presents[definition.SpriteId] = definition;
            stuffTypes.Add(definition.SpriteId);

            if (row.IsDefault)
            {
                defaultStuffTypes.Add(definition.SpriteId);
            }
        }

        // The dialog appends one "default" box to the box row whether or not this list has anything
        // in it -- an empty list leaves an uninitialised zero there, which draws a blank tile that
        // also collides with box type 0. So with none flagged it gets the first parcel.
        if (defaultStuffTypes.Count == 0 && stuffTypes.Count > 0)
        {
            defaultStuffTypes.Add(stuffTypes[0]);
        }

        _presents = presents;
        Configuration = new GiftWrappingConfigurationSnapshot(
            // Off when this hotel ships no parcels at all: with an empty stuff type list the dialog
            // would open on a selection that does not exist.
            IsWrappingEnabled: stuffTypes.Count > 0,
            // Wrapping is free here, so the client is told so. The official server charges one
            // credit; that is a hotel decision, not a protocol one.
            WrappingPrice: 0,
            StuffTypes: [.. stuffTypes],
            BoxTypes: ArtIndices(rows, GiftWrappingKind.Box),
            RibbonTypes: ArtIndices(rows, GiftWrappingKind.Ribbon),
            DefaultStuffTypes: [.. defaultStuffTypes]
        );

        _logger.LogInformation(
            "Loaded gift wrappings: {Parcels} parcels, {Boxes} boxes, {Ribbons} ribbons",
            stuffTypes.Count,
            Configuration.BoxTypes.Length,
            Configuration.RibbonTypes.Length
        );
    }

    private static int[] ArtIndices(List<CatalogGiftWrappingEntity> rows, GiftWrappingKind kind) =>
        [.. rows.Where(r => r.Kind == kind && r.ArtIndex.HasValue).Select(r => r.ArtIndex!.Value)];
}
