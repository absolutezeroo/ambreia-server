using Vortex.Primitives.Rpg.Items.Enums;

namespace Vortex.Rpg.Items;

/// <summary>
/// One copy of an item a character holds (spec Objets §5): a stack of 20 is one copy of quantity
/// 20. Mutable, owned by a <see cref="CharacterInventory"/>; <see cref="Dirty"/> and
/// <see cref="Deleted"/> say what the next write has to do.
/// </summary>
public sealed class ItemInstance
{
    /// <summary>The <c>rpg_items</c> id; 0 until the copy is first written.</summary>
    public int Id { get; set; }

    public required string DefinitionId { get; set; }

    public int Quantity { get; set; }

    /// <summary>Null for an item that never wears out.</summary>
    public int? Durability { get; set; }

    public bool Bound { get; set; }

    public ItemContainer Container { get; set; }

    /// <summary>The bag case, the equipment slot (<see cref="EquipmentSlots"/>), or the parcel's number.</summary>
    public int Slot { get; set; }

    /// <summary>Changed since the last write.</summary>
    public bool Dirty { get; set; }

    /// <summary>Gone from the character; kept until the write that soft-deletes its row.</summary>
    public bool Deleted { get; set; }

    public ItemInstance Copy() => (ItemInstance)MemberwiseClone();
}
