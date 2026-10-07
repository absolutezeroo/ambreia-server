using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Database.Entities.Pets;
using Vortex.Primitives.Hosting;
using Vortex.Primitives.Pets.Providers;
using Vortex.Primitives.Pets.Snapshots;

namespace Vortex.Players.Providers;

public sealed class PetSkinProvider(
    IDbContextFactory<VortexDbContext> dbCtxFactory,
    ILogger<IPetSkinProvider> logger
) : IPetSkinProvider, IReferenceDataProvider
{
    public int LoadStage => 0;

    private ImmutableDictionary<int, ImmutableArray<PetSkinEntry>> _byType = ImmutableDictionary<
        int,
        ImmutableArray<PetSkinEntry>
    >.Empty;
    private readonly IDbContextFactory<VortexDbContext> _dbCtxFactory = dbCtxFactory;
    private readonly ILogger<IPetSkinProvider> _logger = logger;

    public IReadOnlyList<PetSkinEntry> GetSkinsForType(int petType)
    {
        if (_byType.TryGetValue(petType, out ImmutableArray<PetSkinEntry> entries))
        {
            return entries;
        }

        return [];
    }

    public async Task ReloadAsync(CancellationToken ct)
    {
        VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        try
        {
            List<PetSkinEntity> entities = await dbCtx
                .PetSkins.AsNoTracking()
                .ToListAsync(ct)
                .ConfigureAwait(false);

            ImmutableDictionary<int, ImmutableArray<PetSkinEntry>> byType = entities
                .GroupBy(e => e.PetType)
                .ToImmutableDictionary(
                    g => g.Key,
                    g =>
                        g.Select(entity => new PetSkinEntry
                            {
                                PetType = entity.PetType,
                                SkinIndex = entity.SkinIndex,
                                Color = entity.Color,
                                Sellable = entity.Sellable,
                                Rare = entity.Rare,
                            })
                            .ToImmutableArray()
                );

            _byType = byType;

            _logger.LogInformation(
                "Loaded pet palettes: {Count} entries across {Types} types",
                entities.Count,
                byType.Count
            );
        }
        finally
        {
            await dbCtx.DisposeAsync().ConfigureAwait(false);
        }
    }
}
