namespace Vortex.Primitives.Inventory.Enums;

/// <summary>
/// The tabs the client splits an inventory into, and the keys it files unseen items under.
/// </summary>
/// <remarks>
/// Values are the client's own, from <c>com/sulake/habbo/inventory/enum/_SafeCls_1779.as</c> in
/// WIN63-202607011411-782849652. Only 1-5 are listed there as INVENTORY_CATEGORIES; 6 and 7 exist
/// as constants beside them. A 2026-09-18 capture also shows the official server using a category
/// 10 that this build's enum does not name, so the list is not known to be closed.
/// </remarks>
public enum InventoryCategory
{
    OwnedFurni = 1,
    RentedFurni = 2,
    Pet = 3,
    Badge = 4,
    Bot = 5,
    Games = 6,
    Collectibles = 7,
}
