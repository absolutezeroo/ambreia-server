namespace Vortex.Rpg.Items;

/// <summary>How many bag cases a character has, and how many parcels it may keep (spec Objets §2).</summary>
public sealed record InventoryLimits(int BagSize, int MaxParcels);
