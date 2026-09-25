using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Database.Entities.Catalog;
using Vortex.Primitives.Catalog;
using Vortex.Primitives.Catalog.Admin;
using Vortex.Primitives.Catalog.Enums;
using Vortex.Primitives.Catalog.Providers;
using Vortex.Primitives.Furniture.Providers;

namespace Vortex.Dashboard.API.Admin.Catalogue;

/// <summary>
/// Gift wrapping writes, each followed by a reload of the live list.
/// </summary>
/// <remarks>
/// The reload is not a nicety: <see cref="IGiftWrappingProvider" /> answers every gift dialog the
/// client opens, and a write that skipped it would leave an operator looking at a row the hotel does
/// not offer — the "DB write not reflected in live state" failure AGENTS.md calls out.
/// </remarks>
internal sealed class GiftWrappingAdminService(
    IDbContextFactory<VortexDbContext> dbCtxFactory,
    IGiftWrappingProvider wrappings,
    IFurnitureDefinitionProvider furniture,
    ILogger<GiftWrappingAdminService> logger
) : IGiftWrappingAdminService
{
    public async Task<GiftWrappingAdminResult> CreateAsync(
        GiftWrappingEntrySpec spec,
        CancellationToken ct
    )
    {
        GiftWrappingEntrySpec normalized = Normalize(spec);

        if (Validate(normalized) is { } invalid)
        {
            return invalid;
        }

        await using VortexDbContext dbCtx = await dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        if (await IsListedAsync(dbCtx, normalized, exceptId: 0, ct).ConfigureAwait(false))
        {
            return GiftWrappingAdminResult.Fail("already_listed");
        }

        CatalogGiftWrappingEntity entity = new() { Kind = normalized.Kind };

        Apply(entity, normalized);
        dbCtx.CatalogGiftWrappings.Add(entity);

        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadLiveAsync(ct).ConfigureAwait(false);

        return GiftWrappingAdminResult.Ok(entity.Id);
    }

    public async Task<GiftWrappingAdminResult> UpdateAsync(
        int wrappingId,
        GiftWrappingEntrySpec spec,
        CancellationToken ct
    )
    {
        GiftWrappingEntrySpec normalized = Normalize(spec);

        if (Validate(normalized) is { } invalid)
        {
            return invalid;
        }

        await using VortexDbContext dbCtx = await dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        CatalogGiftWrappingEntity? entity = await dbCtx
            .CatalogGiftWrappings.FirstOrDefaultAsync(w => w.Id == wrappingId, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return GiftWrappingAdminResult.Fail("wrapping_not_found");
        }

        if (await IsListedAsync(dbCtx, normalized, wrappingId, ct).ConfigureAwait(false))
        {
            return GiftWrappingAdminResult.Fail("already_listed");
        }

        Apply(entity, normalized);

        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadLiveAsync(ct).ConfigureAwait(false);

        return GiftWrappingAdminResult.Ok(wrappingId);
    }

    public async Task<GiftWrappingAdminResult> DeleteAsync(int wrappingId, CancellationToken ct)
    {
        await using VortexDbContext dbCtx = await dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        // Soft, and safe to be: the provider reads through the soft-delete filter, and a present
        // already wrapped in a removed parcel is its own furniture row, untouched by this one.
        int rows = await dbCtx
            .CatalogGiftWrappings.Where(w => w.Id == wrappingId)
            .ExecuteUpdateAsync(row => row.SetProperty(w => w.DeletedAt, DateTime.UtcNow), ct)
            .ConfigureAwait(false);

        if (rows == 0)
        {
            return GiftWrappingAdminResult.Fail("wrapping_not_found");
        }

        await ReloadLiveAsync(ct).ConfigureAwait(false);

        return GiftWrappingAdminResult.Ok(wrappingId);
    }

    public async Task<GiftWrappingAdminResult> ReloadAsync(CancellationToken ct)
    {
        await ReloadLiveAsync(ct).ConfigureAwait(false);

        return GiftWrappingAdminResult.Ok(0);
    }

    /// <summary>
    /// Drops the field the kind does not use, so a row can never carry both — the provider reads
    /// one or the other by kind, and a stale value in the other column would only mislead whoever
    /// reads the table by hand.
    /// </summary>
    private static GiftWrappingEntrySpec Normalize(GiftWrappingEntrySpec spec) =>
        spec.Kind == GiftWrappingKind.Parcel
            ? spec with
            {
                FurnitureName = spec.FurnitureName?.Trim(),
                ArtIndex = null,
            }
            : spec with
            {
                FurnitureName = null,
                IsDefault = false,
            };

    /// <summary>
    /// A parcel naming no furniture is dropped by the provider — the client would draw nothing — so
    /// it is refused here, where the operator can still see why, rather than vanishing silently.
    /// </summary>
    private GiftWrappingAdminResult? Validate(GiftWrappingEntrySpec spec)
    {
        if (!Enum.IsDefined(spec.Kind))
        {
            return GiftWrappingAdminResult.Fail("invalid_kind");
        }

        if (spec.Kind == GiftWrappingKind.Parcel)
        {
            if (string.IsNullOrWhiteSpace(spec.FurnitureName))
            {
                return GiftWrappingAdminResult.Fail("furniture_required");
            }

            if (furniture.TryGetDefinitionByName(spec.FurnitureName) is null)
            {
                return GiftWrappingAdminResult.Fail("furniture_not_found");
            }

            return null;
        }

        return spec.ArtIndex is null or < 0
            ? GiftWrappingAdminResult.Fail("art_index_required")
            : null;
    }

    /// <summary>The same parcel or index twice would draw the same choice twice in the dialog.</summary>
    private static Task<bool> IsListedAsync(
        VortexDbContext dbCtx,
        GiftWrappingEntrySpec spec,
        int exceptId,
        CancellationToken ct
    ) =>
        dbCtx.CatalogGiftWrappings.AnyAsync(
            w =>
                w.Id != exceptId
                && w.Kind == spec.Kind
                && w.FurnitureName == spec.FurnitureName
                && w.ArtIndex == spec.ArtIndex,
            ct
        );

    private static void Apply(CatalogGiftWrappingEntity entity, GiftWrappingEntrySpec spec)
    {
        entity.Kind = spec.Kind;
        entity.FurnitureName = spec.FurnitureName;
        entity.ArtIndex = spec.ArtIndex;
        entity.IsDefault = spec.IsDefault;
        entity.SortOrder = spec.SortOrder;
    }

    private async Task ReloadLiveAsync(CancellationToken ct)
    {
        try
        {
            await wrappings.ReloadAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The row is committed either way; saying so is the difference between an operator
            // wondering why the dialog did not change and one who knows to press reload.
            logger.LogError(
                ex,
                "The gift wrappings were written but could not be reloaded; the hotel is still offering the previous set."
            );
        }
    }
}
