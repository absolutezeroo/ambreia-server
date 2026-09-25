using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Furniture.Snapshots;
using Vortex.Primitives.Snapshots.Catalog;

namespace Vortex.Primitives.Catalog.Providers;

/// <summary>
/// The wrapping choices the gift dialog is offered, read from <c>catalog_gift_wrappings</c>, and
/// the furniture a chosen one resolves to.
/// </summary>
/// <remarks>
/// <para>
/// A stuff type is a furniture <em>sprite id</em>, not an index. The dialog hands the value straight
/// to <c>IRoomEngine.getFurnitureImage</c> to draw the parcel preview
/// (<c>PurchaseConfirmationDialog.as:900</c>) and sends that same value back on purchase, so both
/// halves of the exchange have to speak in this hotel's own ids — which is why parcel rows name
/// their furniture and are resolved here rather than stored as numbers.
/// </para>
/// <para>
/// Box and ribbon types are the opposite: pure client art indices, turned into the localisation key
/// <c>catalog.gift_wrapping_new.box.&lt;n&gt;</c> (line 678). A new one needs its art and that key
/// in the client; the server only lists it.
/// </para>
/// </remarks>
public interface IGiftWrappingProvider
{
    /// <summary>What <c>GiftWrappingConfigurationEvent</c> sends, minus nothing.</summary>
    GiftWrappingConfigurationSnapshot Configuration { get; }

    /// <summary>
    /// The parcel behind a stuff type the buyer chose, or null when it names no offered parcel —
    /// which the caller must treat as "hand the gift over unwrapped" rather than as a reason to fail
    /// a purchase that has already been paid for.
    /// </summary>
    FurnitureDefinitionSnapshot? ResolvePresent(int stuffTypeId);

    /// <summary>Rebuilds the list from the table without an emulator restart.</summary>
    Task ReloadAsync(CancellationToken ct);
}
