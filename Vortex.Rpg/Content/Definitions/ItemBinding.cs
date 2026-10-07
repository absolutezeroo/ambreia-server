namespace Vortex.Rpg.Content.Definitions;

/// <summary>When an item becomes bound to its owner (spec Objets §2). Values are never renumbered.</summary>
public enum ItemBinding
{
    None = 0,
    OnPickup = 1,
    OnEquip = 2,
}
