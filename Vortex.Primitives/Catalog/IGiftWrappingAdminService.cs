using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Catalog.Admin;

namespace Vortex.Primitives.Catalog;

/// <summary>
/// The gift dialog's parcels, boxes and ribbons (<c>catalog_gift_wrappings</c>), as an operator
/// edits them. Every write reloads <see cref="Providers.IGiftWrappingProvider"/>, so the next
/// player to open the dialog sees the change without an emulator restart.
/// </summary>
public interface IGiftWrappingAdminService
{
    Task<GiftWrappingAdminResult> CreateAsync(GiftWrappingEntrySpec spec, CancellationToken ct);

    Task<GiftWrappingAdminResult> UpdateAsync(
        int wrappingId,
        GiftWrappingEntrySpec spec,
        CancellationToken ct
    );

    /// <summary>
    /// Soft-deletes a choice. Presents already wrapped in a removed parcel keep their furniture; only
    /// the dialog stops offering it.
    /// </summary>
    Task<GiftWrappingAdminResult> DeleteAsync(int wrappingId, CancellationToken ct);

    /// <summary>Rebuilds the live list from the database without an emulator restart.</summary>
    Task<GiftWrappingAdminResult> ReloadAsync(CancellationToken ct);
}
