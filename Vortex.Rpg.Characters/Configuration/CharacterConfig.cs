namespace Vortex.Rpg.Characters.Configuration;

/// <summary>Character persistence and XP limits (spec Personnage §5, §7).</summary>
public sealed class CharacterConfig
{
    public const string SECTION_NAME = "Vortex:Characters";

    /// <summary>How often dirty progress (XP, last zone) is written; a level is written at once.</summary>
    public int FlushIntervalMs { get; init; } = 30_000;

    /// <summary>The most XP one grant may give: anything above is refused and logged.</summary>
    public int MaxXpPerGrant { get; init; } = 100_000;
}
