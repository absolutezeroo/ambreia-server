namespace Vortex.Rpg.Content.Definitions;

/// <summary>What an item is (spec Objets §4). Values are never renumbered.</summary>
public enum ItemCategory
{
    Equipment = 0,
    Consumable = 1,
    Material = 2,
    Furniture = 3,
    Mount = 4,
    Cosmetic = 5,
    Quest = 6,

    /// <summary>A pickaxe, an axe, a sickle (content authoring §7): carried, not worn; what it opens
    /// (gathering) comes with its own spec.</summary>
    Tool = 7,
}
