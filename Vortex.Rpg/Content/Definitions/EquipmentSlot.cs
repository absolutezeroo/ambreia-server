namespace Vortex.Rpg.Content.Definitions;

/// <summary>
/// Where an equipment goes (spec Objets §2). <see cref="Ring"/> fits either ring slot of a
/// character. Values are never renumbered.
/// </summary>
public enum EquipmentSlot
{
    Head = 0,
    Chest = 1,
    Legs = 2,
    Feet = 3,
    Hands = 4,
    Weapon = 5,
    Offhand = 6,
    Neck = 7,
    Ring = 8,
}
