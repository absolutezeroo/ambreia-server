using System.Collections.Generic;
using System.Collections.Immutable;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Furniture.Snapshots;

namespace Vortex.Primitives.Catalog;

/// <summary>
/// The wrapping choices the gift dialog is offered, and the furniture a chosen one resolves to.
/// </summary>
/// <remarks>
/// <para>
/// A stuff type is a furniture <em>class id</em>, not an index. The dialog hands the value straight
/// to <c>IRoomEngine.getFurnitureImage</c> to draw the parcel preview
/// (<c>PurchaseConfirmationDialog.as:900</c>) and sends that same value back on purchase, so both
/// halves of the exchange have to speak in this hotel's own class ids — which is why they are
/// looked up here rather than written down.
/// </para>
/// <para>
/// Box and ribbon types are the opposite: pure client art indices, turned into the localisation key
/// <c>catalog.gift_wrapping_new.box.&lt;n&gt;</c> (line 678). Nothing on the server resolves them,
/// so they are constants, copied from what the official server advertises.
/// </para>
/// </remarks>
public static class GiftWrapping
{
    /// <summary>
    /// The ordinary parcels, in the order the dialog shows them. <c>present_gen</c> leads because
    /// the client takes <c>stuffTypes[0]</c> as its opening selection.
    /// </summary>
    public static readonly ImmutableArray<string> PresentClassNames =
    [
        "present_gen",
        "present_gen1",
        "present_gen2",
        "present_gen3",
        "present_gen4",
        "present_gen5",
        "present_gen6",
    ];

    /// <summary>
    /// Box art indices, mirroring the official server (capture 2026-09-18). 7 is skipped there and
    /// is skipped here for the same reason: it is the one index whose art the client does not ship.
    /// </summary>
    public static readonly ImmutableArray<int> BoxTypes = [0, 1, 2, 3, 4, 5, 6, 8];

    /// <summary>Ribbon art indices, likewise straight from the official server.</summary>
    public static readonly ImmutableArray<int> RibbonTypes = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

    /// <summary>
    /// Every parcel this hotel actually ships, as the class ids the client expects. Names that
    /// resolve to no furniture are dropped rather than sent as a hole: the client would ask its
    /// room engine to draw them and get nothing back.
    /// </summary>
    public static ImmutableArray<int> ResolveStuffTypes(IFurnitureDefinitionProvider definitions)
    {
        List<int> spriteIds = [];

        foreach (string name in PresentClassNames)
        {
            FurnitureDefinitionSnapshot? definition = definitions.TryGetDefinitionByName(name);

            if (definition is not null)
            {
                spriteIds.Add(definition.SpriteId);
            }
        }

        return [.. spriteIds];
    }

    /// <summary>
    /// The parcel behind a stuff type the buyer chose, or null when it names nothing this hotel
    /// ships — which the caller must treat as "hand the gift over unwrapped" rather than as a
    /// reason to fail a purchase that has already been paid for.
    /// </summary>
    public static FurnitureDefinitionSnapshot? ResolvePresent(
        IFurnitureDefinitionProvider definitions,
        int stuffTypeId
    )
    {
        foreach (string name in PresentClassNames)
        {
            FurnitureDefinitionSnapshot? definition = definitions.TryGetDefinitionByName(name);

            if (definition is not null && definition.SpriteId == stuffTypeId)
            {
                return definition;
            }
        }

        return null;
    }
}
