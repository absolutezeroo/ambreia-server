namespace Vortex.Primitives.Observability;

/// <summary>A point in an item's lifecycle.</summary>
public enum ItemEventType
{
    Created,
    CatalogPurchase,
    Placed,
    Moved,
    PickedUp,
    Traded,
    OwnerChanged,
    Deleted,
    StaffAction,
    AnomalyFlagged,

    // RPG items (spec Objets §13): their item_id is an rpg_items id, not a furniture id.
    RpgGranted,
    RpgDestroyed,
    RpgRemoved,
    RpgBound,
}
