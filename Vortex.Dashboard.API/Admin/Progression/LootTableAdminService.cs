using System;
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
using Vortex.Primitives.Loot.Admin;
using Vortex.Primitives.Orleans;

namespace Vortex.Dashboard.API.Admin.Progression;

/// <summary>
/// CRUD for the prize pool tables. A plain singleton (not a grain) opening a short-lived
/// <see cref="VortexDbContext"/> per call: these rows aren't grain-owned and admin writes are
/// low-frequency. The live pools come from the kept-alive <c>LootTableManagerGrain</c> cache, which
/// is only rebuilt via its <c>ReloadAsync</c>, so every write reloads it afterwards — the "DB write
/// not reflected in live state" bug class called out in AGENTS.md.
/// </summary>
internal sealed class LootTableAdminService(
    IDbContextFactory<VortexDbContext> dbContextFactory,
    IGrainFactory grainFactory,
    ILogger<LootTableAdminService> logger
) : ILootTableAdminService
{
    /// <summary>Loot types the client's reward window can draw. Anything else would award silently
    /// into a blank dialog, so it is refused at the admin boundary rather than at draw time.</summary>
    private static readonly ProductType[] DrawableProductTypes =
    [
        ProductType.Floor,
        ProductType.Wall,
        ProductType.Effect,
        ProductType.Premium,
    ];

    public async Task<LootAdminResult> CreatePoolAsync(LootTableSpec spec, CancellationToken ct)
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        string code = NormalizeCode(spec.Code);

        if (code.Length == 0)
        {
            return LootAdminResult.Fail("pool_code_required");
        }

        if (await db.LootTables.AnyAsync(p => p.Code == code, ct).ConfigureAwait(false))
        {
            return LootAdminResult.Fail("pool_code_taken");
        }

        LootTableEntity entity = new()
        {
            Code = code,
            Name = (spec.Name ?? string.Empty).Trim(),
            Variants = NormalizeVariantSet(spec.Variants),
            Notes = (spec.Notes ?? string.Empty).Trim(),
            Enabled = spec.Enabled,
        };

        db.LootTables.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAsync(ct).ConfigureAwait(false);

        return LootAdminResult.Ok(entity.Id);
    }

    public async Task<LootAdminResult> UpdatePoolAsync(
        int poolId,
        LootTableSpec spec,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        LootTableEntity? entity = await db
            .LootTables.FirstOrDefaultAsync(p => p.Id == poolId, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return LootAdminResult.Fail("pool_not_found");
        }

        string code = NormalizeCode(spec.Code);

        if (code.Length == 0)
        {
            return LootAdminResult.Fail("pool_code_required");
        }

        if (
            await db
                .LootTables.AnyAsync(p => p.Code == code && p.Id != poolId, ct)
                .ConfigureAwait(false)
        )
        {
            return LootAdminResult.Fail("pool_code_taken");
        }

        entity.Code = code;
        entity.Name = (spec.Name ?? string.Empty).Trim();
        entity.Variants = NormalizeVariantSet(spec.Variants);
        entity.Notes = (spec.Notes ?? string.Empty).Trim();
        entity.Enabled = spec.Enabled;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAsync(ct).ConfigureAwait(false);

        return LootAdminResult.Ok(entity.Id);
    }

    public async Task<LootAdminResult> DeletePoolAsync(int poolId, CancellationToken ct)
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        LootTableEntity? entity = await db
            .LootTables.FirstOrDefaultAsync(p => p.Id == poolId, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return LootAdminResult.Fail("pool_not_found");
        }

        db.LootTables.Remove(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAsync(ct).ConfigureAwait(false);

        return LootAdminResult.Ok(poolId);
    }

    public async Task<LootAdminResult> CreateEntryAsync(LootEntrySpec spec, CancellationToken ct)
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        (LootTableEntity? pool, string? error) = await ResolvePoolAsync(db, spec, ct)
            .ConfigureAwait(false);

        if (pool is null)
        {
            return LootAdminResult.Fail(error!);
        }

        LootTableEntryEntity entity = new()
        {
            LootTableEntityId = pool.Id,
            Variant = LootVariants.Normalize(spec.Variant),
            ProductType = spec.ProductType,
            FurnitureDefinitionEntityId = spec.FurnitureDefinitionId,
            ExtraParam = (spec.ExtraParam ?? string.Empty).Trim(),
            Weight = Math.Max(1, spec.Weight),
            Enabled = spec.Enabled,
        };

        db.LootTableEntries.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAsync(ct).ConfigureAwait(false);

        return LootAdminResult.Ok(entity.Id);
    }

    public async Task<LootAdminResult> UpdateEntryAsync(
        int entryId,
        LootEntrySpec spec,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        LootTableEntryEntity? entity = await db
            .LootTableEntries.FirstOrDefaultAsync(e => e.Id == entryId, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return LootAdminResult.Fail("entry_not_found");
        }

        (LootTableEntity? pool, string? error) = await ResolvePoolAsync(db, spec, ct)
            .ConfigureAwait(false);

        if (pool is null)
        {
            return LootAdminResult.Fail(error!);
        }

        entity.LootTableEntityId = pool.Id;
        entity.Variant = LootVariants.Normalize(spec.Variant);
        entity.ProductType = spec.ProductType;
        entity.FurnitureDefinitionEntityId = spec.FurnitureDefinitionId;
        entity.ExtraParam = (spec.ExtraParam ?? string.Empty).Trim();
        entity.Weight = Math.Max(1, spec.Weight);
        entity.Enabled = spec.Enabled;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAsync(ct).ConfigureAwait(false);

        return LootAdminResult.Ok(entity.Id);
    }

    public async Task<LootAdminResult> DeleteEntryAsync(int entryId, CancellationToken ct)
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        LootTableEntryEntity? entity = await db
            .LootTableEntries.FirstOrDefaultAsync(e => e.Id == entryId, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return LootAdminResult.Fail("entry_not_found");
        }

        db.LootTableEntries.Remove(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAsync(ct).ConfigureAwait(false);

        return LootAdminResult.Ok(entryId);
    }

    public async Task<LootAdminResult> CreateBindingAsync(
        LootBindingSpec spec,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        (LootTableEntity? pool, string? error) = await ResolveBindingPoolAsync(db, spec, ct)
            .ConfigureAwait(false);

        if (pool is null)
        {
            return LootAdminResult.Fail(error!);
        }

        // One binding per definition: two would make which pool a furniture draws from depend on
        // row order, which is exactly the kind of thing nobody notices until the wrong prize drops.
        if (
            await db
                .LootTableBindings.AnyAsync(
                    b => b.FurnitureDefinitionEntityId == spec.FurnitureDefinitionId,
                    ct
                )
                .ConfigureAwait(false)
        )
        {
            return LootAdminResult.Fail("binding_already_exists");
        }

        LootTableBindingEntity entity = new()
        {
            FurnitureDefinitionEntityId = spec.FurnitureDefinitionId,
            LootTableEntityId = pool.Id,
            HitsRequired = Math.Max(1, spec.HitsRequired),
            Enabled = spec.Enabled,
        };

        db.LootTableBindings.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAsync(ct).ConfigureAwait(false);

        return LootAdminResult.Ok(entity.Id);
    }

    public async Task<LootAdminResult> UpdateBindingAsync(
        int bindingId,
        LootBindingSpec spec,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        LootTableBindingEntity? entity = await db
            .LootTableBindings.FirstOrDefaultAsync(b => b.Id == bindingId, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return LootAdminResult.Fail("binding_not_found");
        }

        (LootTableEntity? pool, string? error) = await ResolveBindingPoolAsync(db, spec, ct)
            .ConfigureAwait(false);

        if (pool is null)
        {
            return LootAdminResult.Fail(error!);
        }

        entity.LootTableEntityId = pool.Id;
        entity.HitsRequired = Math.Max(1, spec.HitsRequired);
        entity.Enabled = spec.Enabled;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAsync(ct).ConfigureAwait(false);

        return LootAdminResult.Ok(entity.Id);
    }

    public async Task<LootAdminResult> DeleteBindingAsync(int bindingId, CancellationToken ct)
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        LootTableBindingEntity? entity = await db
            .LootTableBindings.FirstOrDefaultAsync(b => b.Id == bindingId, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return LootAdminResult.Fail("binding_not_found");
        }

        db.LootTableBindings.Remove(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAsync(ct).ConfigureAwait(false);

        return LootAdminResult.Ok(bindingId);
    }

    private static async Task<(LootTableEntity? Pool, string? Error)> ResolveBindingPoolAsync(
        VortexDbContext db,
        LootBindingSpec spec,
        CancellationToken ct
    )
    {
        if (spec.FurnitureDefinitionId <= 0)
        {
            return (null, "furniture_definition_required");
        }

        if (
            !await db
                .FurnitureDefinitions.AnyAsync(f => f.Id == spec.FurnitureDefinitionId, ct)
                .ConfigureAwait(false)
        )
        {
            return (null, "furniture_definition_not_found");
        }

        LootTableEntity? pool = await db
            .LootTables.FirstOrDefaultAsync(p => p.Code == NormalizeCode(spec.PoolCode), ct)
            .ConfigureAwait(false);

        return pool is null ? (null, "pool_not_found") : (pool, null);
    }

    public async Task<LootAdminResult> ReloadCacheAsync(CancellationToken ct)
    {
        await ReloadAsync(ct).ConfigureAwait(false);

        return LootAdminResult.Ok(0);
    }

    private static string NormalizeCode(string? code) =>
        string.IsNullOrWhiteSpace(code) ? string.Empty : code.Trim().ToLowerInvariant();

    private static string NormalizeVariantSet(string? variants) =>
        string.Join(',', LootVariants.ParseSet(variants));

    /// <summary>
    /// Resolves and validates the target pool for an entry write. Returns the pool, or null plus the
    /// error code the caller should surface.
    /// </summary>
    private static async Task<(LootTableEntity? Pool, string? Error)> ResolvePoolAsync(
        VortexDbContext db,
        LootEntrySpec spec,
        CancellationToken ct
    )
    {
        string poolCode = NormalizeCode(spec.PoolCode);

        LootTableEntity? pool = await db
            .LootTables.FirstOrDefaultAsync(p => p.Code == poolCode, ct)
            .ConfigureAwait(false);

        if (pool is null)
        {
            return (null, "pool_not_found");
        }

        if (!DrawableProductTypes.Contains(spec.ProductType))
        {
            return (null, "product_type_not_drawable");
        }

        if (spec.Weight <= 0)
        {
            return (null, "weight_must_be_positive");
        }

        // A variant is optional (empty = any), but one outside the pool's declared set would silently
        // widen back to "any" at load time and hand the prize out far more often than intended.
        ImmutableArray<string> set = LootVariants.ParseSet(pool.Variants);
        string variant = LootVariants.Normalize(spec.Variant);

        if (
            variant.Length > 0
            && !set.IsDefaultOrEmpty
            && !set.Contains(variant, StringComparer.Ordinal)
        )
        {
            return (null, "variant_not_in_pool_set");
        }

        if (spec.ProductType is ProductType.Floor or ProductType.Wall)
        {
            if (spec.FurnitureDefinitionId <= 0)
            {
                return (null, "furniture_definition_required");
            }

            if (
                !await db
                    .FurnitureDefinitions.AnyAsync(f => f.Id == spec.FurnitureDefinitionId, ct)
                    .ConfigureAwait(false)
            )
            {
                return (null, "furniture_definition_not_found");
            }

            return (pool, null);
        }

        // Effect and club prizes carry their target in ExtraParam; an empty one would award nothing
        // and close the winner's reward window on an error.
        if (string.IsNullOrWhiteSpace(spec.ExtraParam))
        {
            return (null, "extra_param_required");
        }

        string head = spec.ExtraParam.Split(':')[0];

        return int.TryParse(head, out int value) && value > 0
            ? (pool, null)
            : (null, "extra_param_invalid");
    }

    private async Task ReloadAsync(CancellationToken ct)
    {
        try
        {
            await grainFactory.GetLootTableManagerGrain().ReloadAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The DB write already committed -- the live pools are now stale until the next reload or
            // restart. Never swallow this: it is the "DB write not reflected in live state" bug class
            // called out in AGENTS.md.
            logger.LogError(
                ex,
                "Loot table cache reload failed after an admin write committed -- live pools are now stale until the next reload or restart"
            );
            throw;
        }
    }
}
