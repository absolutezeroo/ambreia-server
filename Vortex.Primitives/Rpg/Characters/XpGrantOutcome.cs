namespace Vortex.Primitives.Rpg.Characters;

/// <summary>The answer to an XP grant.</summary>
public enum XpGrantOutcome
{
    Granted = 0,

    /// <summary>Zero, negative, or over <c>CharacterConfig.MaxXpPerGrant</c>; logged.</summary>
    Refused = 1,
    NoCharacter = 2,

    /// <summary>A behaviour of <c>XpGainingEvent</c> cancelled it.</summary>
    Cancelled = 3,
    AtMaxLevel = 4,
}
