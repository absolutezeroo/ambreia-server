namespace Vortex.Rpg.Items;

/// <summary>How much of a copy an operation touched: added by a grant, taken by a removal.</summary>
public sealed record ItemQuantity(ItemInstance Item, int Quantity);
