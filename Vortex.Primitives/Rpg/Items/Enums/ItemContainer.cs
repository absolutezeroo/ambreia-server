namespace Vortex.Primitives.Rpg.Items.Enums;

/// <summary>Where a copy of an item lies (spec Objets §5). Stored in <c>rpg_items.container</c>:
/// never renumbered.</summary>
public enum ItemContainer
{
    Bag = 0,
    Equipment = 1,
    Parcel = 2,
}
