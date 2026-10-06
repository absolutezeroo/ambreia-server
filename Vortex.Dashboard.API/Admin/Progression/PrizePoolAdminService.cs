using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Entities.Prizes;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Prizes;
using Vortex.Primitives.Prizes.Admin;

namespace Vortex.Dashboard.API.Admin.Progression;

/// <summary>
/// CRUD for the prize pool tables. A plain singleton (not a grain) opening a short-lived
/// <see cref="VortexDbContext"/> per call: these rows aren't grain-owned and admin writes are
/// low-frequency. The live pools come from the kept-alive <c>PrizePoolManagerGrain</c> cache, which
/// is only rebuilt via its <c>ReloadAsync</c>, so every write reloads it afterwards — the "DB write
/// not reflected in live state" bug class called out in AGENTS.md.
/// </summary>
internal sealed class PrizePoolAdminService(
    IDbContextFactory<VortexDbContext> dbContextFactory,
    IGrainFactory grainFactory,
    ILogger<PrizePoolAdminService> logger
) : IPrizePoolAdminService
{
    /// <summary>Prize types the client's reward window can draw. Anything else would award silently
    /// into a blank dialog, so it is refused at the admin boundary rather than at draw time.</summary>
    private static readonly ProductType[] DrawableProductTypes =
    [
        ProductType.Floor,
        ProductType.Wall,
        ProductType.Effect,
        ProductType.HabboClub,
    ];

    public async Task<PrizeAdminResult> CreatePoolAsync(PrizePoolSpec spec, CancellationToken ct)
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        string code = NormalizeCode(spec.Code);

        if (code.Length == 0)
        {
            return PrizeAdminResult.Fail("pool_code_required");
        }

        if (await db.PrizePools.AnyAsync(p => p.Code == code, ct).ConfigureAwait(false))
        {
            return PrizeAdminResult.Fail("pool_code_taken");
        }

        PrizePoolEntity entity = new()
        {
            Code = code,
            Name = (spec.Name ?? string.Empty).Trim(),
            Variants = NormalizeVariantSet(spec.Variants),
            Notes = (spec.Notes ?? string.Empty).Trim(),
            Enabled = spec.Enabled,
        };

        db.PrizePools.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAsync(ct).ConfigureAwait(false);

        return PrizeAdminResult.Ok(entity.Id);
    }

    public async Task<PrizeAdminResult> UpdatePoolAsync(
        int poolId,
        PrizePoolSpec spec,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        PrizePoolEntity? entity = await db
            .PrizePools.FirstOrDefaultAsync(p => p.Id == poolId, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return PrizeAdminResult.Fail("pool_not_found");
        }

        string code = NormalizeCode(spec.Code);

        if (code.Length == 0)
        {
            return PrizeAdminResult.Fail("pool_code_required");
        }

        if (
            await db
                .PrizePools.AnyAsync(p => p.Code == code && p.Id != poolId, ct)
                .ConfigureAwait(false)
        )
        {
            return PrizeAdminResult.Fail("pool_code_taken");
        }

        entity.Code = code;
        entity.Name = (spec.Name ?? string.Empty).Trim();
        entity.Variants = NormalizeVariantSet(spec.Variants);
        entity.Notes = (spec.Notes ?? string.Empty).Trim();
        entity.Enabled = spec.Enabled;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAsync(ct).ConfigureAwait(false);

        return PrizeAdminResult.Ok(entity.Id);
    }

    public async Task<PrizeAdminResult> DeletePoolAsync(int poolId, CancellationToken ct)
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        PrizePoolEntity? entity = await db
            .PrizePools.FirstOrDefaultAsync(p => p.Id == poolId, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return PrizeAdminResult.Fail("pool_not_found");
        }

        db.PrizePools.Remove(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAsync(ct).ConfigureAwait(false);

        return PrizeAdminResult.Ok(poolId);
    }

    public async Task<PrizeAdminResult> CreateEntryAsync(PrizeEntrySpec spec, CancellationToken ct)
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        (PrizePoolEntity? pool, string? error) = await ResolvePoolAsync(db, spec, ct)
            .ConfigureAwait(false);

        if (pool is null)
        {
            return PrizeAdminResult.Fail(error!);
        }

        PrizePoolEntryEntity entity = new()
        {
            PrizePoolEntityId = pool.Id,
            Variant = PrizeVariants.Normalize(spec.Variant),
            ProductType = spec.ProductType,
            FurnitureDefinitionEntityId = spec.FurnitureDefinitionId,
            ExtraParam = (spec.ExtraParam ?? string.Empty).Trim(),
            Weight = Math.Max(1, spec.Weight),
            Enabled = spec.Enabled,
        };

        db.PrizePoolEntries.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAsync(ct).ConfigureAwait(false);

        return PrizeAdminResult.Ok(entity.Id);
    }

    public async Task<PrizeAdminResult> UpdateEntryAsync(
        int entryId,
        PrizeEntrySpec spec,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        PrizePoolEntryEntity? entity = await db
            .PrizePoolEntries.FirstOrDefaultAsync(e => e.Id == entryId, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return PrizeAdminResult.Fail("entry_not_found");
        }

        (PrizePoolEntity? pool, string? error) = await ResolvePoolAsync(db, spec, ct)
            .ConfigureAwait(false);

        if (pool is null)
        {
            return PrizeAdminResult.Fail(error!);
        }

        entity.PrizePoolEntityId = pool.Id;
        entity.Variant = PrizeVariants.Normalize(spec.Variant);
        entity.ProductType = spec.ProductType;
        entity.FurnitureDefinitionEntityId = spec.FurnitureDefinitionId;
        entity.ExtraParam = (spec.ExtraParam ?? string.Empty).Trim();
        entity.Weight = Math.Max(1, spec.Weight);
        entity.Enabled = spec.Enabled;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAsync(ct).ConfigureAwait(false);

        return PrizeAdminResult.Ok(entity.Id);
    }

    public async Task<PrizeAdminResult> DeleteEntryAsync(int entryId, CancellationToken ct)
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        PrizePoolEntryEntity? entity = await db
            .PrizePoolEntries.FirstOrDefaultAsync(e => e.Id == entryId, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return PrizeAdminResult.Fail("entry_not_found");
        }

        db.PrizePoolEntries.Remove(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAsync(ct).ConfigureAwait(false);

        return PrizeAdminResult.Ok(entryId);
    }

    public async Task<PrizeAdminResult> CreateBindingAsync(
        PrizeBindingSpec spec,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        (PrizePoolEntity? pool, string? error) = await ResolveBindingPoolAsync(db, spec, ct)
            .ConfigureAwait(false);

        if (pool is null)
        {
            return PrizeAdminResult.Fail(error!);
        }

        // One binding per definition: two would make which pool a furniture draws from depend on
        // row order, which is exactly the kind of thing nobody notices until the wrong prize drops.
        if (
            await db
                .PrizePoolBindings.AnyAsync(
                    b => b.FurnitureDefinitionEntityId == spec.FurnitureDefinitionId,
                    ct
                )
                .ConfigureAwait(false)
        )
        {
            return PrizeAdminResult.Fail("binding_already_exists");
        }

        PrizePoolBindingEntity entity = new()
        {
            FurnitureDefinitionEntityId = spec.FurnitureDefinitionId,
            PrizePoolEntityId = pool.Id,
            HitsRequired = Math.Max(1, spec.HitsRequired),
            Enabled = spec.Enabled,
        };

        db.PrizePoolBindings.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAsync(ct).ConfigureAwait(false);

        return PrizeAdminResult.Ok(entity.Id);
    }

    public async Task<PrizeAdminResult> UpdateBindingAsync(
        int bindingId,
        PrizeBindingSpec spec,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        PrizePoolBindingEntity? entity = await db
            .PrizePoolBindings.FirstOrDefaultAsync(b => b.Id == bindingId, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return PrizeAdminResult.Fail("binding_not_found");
        }

        (PrizePoolEntity? pool, string? error) = await ResolveBindingPoolAsync(db, spec, ct)
            .ConfigureAwait(false);

        if (pool is null)
        {
            return PrizeAdminResult.Fail(error!);
        }

        entity.PrizePoolEntityId = pool.Id;
        entity.HitsRequired = Math.Max(1, spec.HitsRequired);
        entity.Enabled = spec.Enabled;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAsync(ct).ConfigureAwait(false);

        return PrizeAdminResult.Ok(entity.Id);
    }

    public async Task<PrizeAdminResult> DeleteBindingAsync(int bindingId, CancellationToken ct)
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        PrizePoolBindingEntity? entity = await db
            .PrizePoolBindings.FirstOrDefaultAsync(b => b.Id == bindingId, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return PrizeAdminResult.Fail("binding_not_found");
        }

        db.PrizePoolBindings.Remove(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAsync(ct).ConfigureAwait(false);

        return PrizeAdminResult.Ok(bindingId);
    }

    private static async Task<(PrizePoolEntity? Pool, string? Error)> ResolveBindingPoolAsync(
        VortexDbContext db,
        PrizeBindingSpec spec,
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

        PrizePoolEntity? pool = await db
            .PrizePools.FirstOrDefaultAsync(p => p.Code == NormalizeCode(spec.PoolCode), ct)
            .ConfigureAwait(false);

        return pool is null ? (null, "pool_not_found") : (pool, null);
    }

    public async Task<PrizeAdminResult> ReloadCacheAsync(CancellationToken ct)
    {
        await ReloadAsync(ct).ConfigureAwait(false);

        return PrizeAdminResult.Ok(0);
    }

    private static string NormalizeCode(string? code) =>
        string.IsNullOrWhiteSpace(code) ? string.Empty : code.Trim().ToLowerInvariant();

    private static string NormalizeVariantSet(string? variants) =>
        string.Join(',', PrizeVariants.ParseSet(variants));

    /// <summary>
    /// Resolves and validates the target pool for an entry write. Returns the pool, or null plus the
    /// error code the caller should surface.
    /// </summary>
    private static async Task<(PrizePoolEntity? Pool, string? Error)> ResolvePoolAsync(
        VortexDbContext db,
        PrizeEntrySpec spec,
        CancellationToken ct
    )
    {
        string poolCode = NormalizeCode(spec.PoolCode);

        PrizePoolEntity? pool = await db
            .PrizePools.FirstOrDefaultAsync(p => p.Code == poolCode, ct)
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
        ImmutableArray<string> set = PrizeVariants.ParseSet(pool.Variants);
        string variant = PrizeVariants.Normalize(spec.Variant);

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
            await grainFactory.GetPrizePoolManagerGrain().ReloadAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The DB write already committed -- the live pools are now stale until the next reload or
            // restart. Never swallow this: it is the "DB write not reflected in live state" bug class
            // called out in AGENTS.md.
            logger.LogError(
                ex,
                "Prize pool cache reload failed after an admin write committed -- live pools are now stale until the next reload or restart"
            );
            throw;
        }
    }
}
