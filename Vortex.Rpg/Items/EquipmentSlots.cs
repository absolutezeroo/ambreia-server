using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Items;

/// <summary>
/// The ten equipment slots of a character (spec Objets §2): 0–7 are the <see cref="EquipmentSlot"/>
/// values from <c>Head</c> to <c>Neck</c>, then the two rings.
/// </summary>
public static class EquipmentSlots
{
    public const int Ring1 = 8;
    public const int Ring2 = 9;
    public const int Count = 10;

    public static bool IsValid(int slot) => slot is >= 0 and < Count;
}
