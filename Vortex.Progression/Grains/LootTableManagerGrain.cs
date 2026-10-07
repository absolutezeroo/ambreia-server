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
using Vortex.Database.Entities.Loot;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Loot;
using Vortex.Primitives.Loot.Grains;
using Vortex.Primitives.Loot.Snapshots;

namespace Vortex.Progression.Grains;

/// <summary>
/// Caches every prize pool for the lifetime of the kept-alive singleton. The pools are read on each
/// draw, so re-querying them per open would put a table scan on the hot path for data that changes
/// only when an admin edits it.
/// </summary>
[KeepAlive]
internal sealed class LootTableManagerGrain(
    IDbContextFactory<VortexDbContext> dbCtxFactory,
    ILogger<LootTableManagerGrain> logger
) : Grain, ILootTableManagerGrain
{
    private readonly IDbContextFactory<VortexDbContext> _dbCtxFactory = dbCtxFactory;
    private readonly ILogger<LootTableManagerGrain> _logger = logger;

    /// <summary>Loot types the client's reward window can actually draw; anything else would show a
    /// blank dialog, so such rows are dropped at load time with a warning rather than silently
    /// handed out.</summary>
    private static readonly ProductType[] DrawableProductTypes =
    [
        ProductType.Floor,
        ProductType.Wall,
        ProductType.Effect,
        ProductType.Premium,
    ];

    private ImmutableArray<LootEntrySnapshot> _entries = [];
    private Dictionary<int, LootBindingSnapshot> _bindingsByDefinitionId = [];
    private bool _loaded;

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await LoadAsync(ct).ConfigureAwait(true);
        await base.OnActivateAsync(ct).ConfigureAwait(true);
    }

    public async Task<LootEntrySnapshot?> PickAsync(
        string poolCode,
        string variant,
        CancellationToken ct
    )
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(true);

        LootEntrySnapshot? entry = LootPicker.Pick(_entries, poolCode, variant);

        if (entry is null)
        {
            _logger.LogWarning(
                "Loot table '{PoolCode}' has no enabled entry for variant '{Variant}'; nothing can be awarded.",
                poolCode,
                variant
            );
        }

        return entry;
    }

    public async Task<LootBindingSnapshot?> GetBindingAsync(
        int furnitureDefinitionId,
        CancellationToken ct
    )
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(true);

        return _bindingsByDefinitionId.GetValueOrDefault(furnitureDefinitionId);
    }

    public Task ReloadAsync(CancellationToken ct) => LoadAsync(ct);

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

            List<LootTableEntity> pools = await dbCtx
                .LootTables.AsNoTracking()
                .Where(p => p.Enabled && p.DeletedAt == null)
                .ToListAsync(ct)
                .ConfigureAwait(true);

            Dictionary<int, LootTableEntity> poolsById = pools.ToDictionary(p => p.Id);

            List<LootTableEntryEntity> entryRows = await dbCtx
                .LootTableEntries.AsNoTracking()
                .Where(e => e.Enabled && e.Weight > 0 && e.DeletedAt == null)
                .OrderBy(e => e.Id)
                .ToListAsync(ct)
                .ConfigureAwait(true);

            List<LootEntrySnapshot> entries = [];

            foreach (LootTableEntryEntity row in entryRows)
            {
                // An entry whose pool is disabled or deleted is not an error worth logging on every
                // reload: disabling a pool is exactly how an operator retires a seasonal event.
                if (!poolsById.TryGetValue(row.LootTableEntityId, out LootTableEntity? pool))
                {
                    continue;
                }

                if (!DrawableProductTypes.Contains(row.ProductType))
                {
                    _logger.LogWarning(
                        "Loot entry {Id} has product type {ProductType}, which the reward window cannot draw; skipping it.",
                        row.Id,
                        row.ProductType
                    );

                    continue;
                }

                entries.Add(
                    new LootEntrySnapshot
                    {
                        Id = row.Id,
                        PoolCode = pool.Code,
                        Variant = LootVariants.NormalizeForSet(
                            row.Variant,
                            LootVariants.ParseSet(pool.Variants)
                        ),
                        ProductType = row.ProductType,
                        FurnitureDefinitionId = row.FurnitureDefinitionEntityId,
                        ExtraParam = row.ExtraParam,
                        Weight = row.Weight,
                    }
                );
            }

            List<LootTableBindingEntity> bindingRows = await dbCtx
                .LootTableBindings.AsNoTracking()
                .Where(b => b.Enabled && b.DeletedAt == null)
                .ToListAsync(ct)
                .ConfigureAwait(true);

            Dictionary<int, LootBindingSnapshot> bindings = [];

            foreach (LootTableBindingEntity row in bindingRows)
            {
                if (!poolsById.TryGetValue(row.LootTableEntityId, out LootTableEntity? boundPool))
                {
                    continue;
                }

                bindings[row.FurnitureDefinitionEntityId] = new LootBindingSnapshot
                {
                    PoolCode = boundPool.Code,
                    PoolId = boundPool.Id,
                    // A binding at zero would make the furniture pay out on activation, before
                    // anyone touched it; one hit is the floor.
                    HitsRequired = Math.Max(1, row.HitsRequired),
                };
            }

            _entries = [.. entries];
            _bindingsByDefinitionId = bindings;
            _loaded = true;

            _logger.LogInformation(
                "Loaded {PoolCount} prize pools, {EntryCount} entries and {BindingCount} furniture bindings into cache.",
                pools.Count,
                _entries.Length,
                _bindingsByDefinitionId.Count
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load prize pool reference data.");
        }
    }
}
