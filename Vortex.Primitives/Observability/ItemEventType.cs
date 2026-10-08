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
}
