namespace Vortex.Rpg.Characters.Configuration;

/// <summary>Character persistence and XP limits (spec Personnage §5, §7).</summary>
public sealed class CharacterConfig
{
    public const string SECTION_NAME = "Vortex:Characters";

    /// <summary>How often dirty progress (XP, last zone) is written; a level is written at once.</summary>
    public int FlushIntervalMs { get; init; } = 30_000;

    /// <summary>The most XP one grant may give: anything above is refused and logged.</summary>
    public int MaxXpPerGrant { get; init; } = 100_000;

    /// <summary>Characters one account may hold on the character screen (spec F3a §5.3).</summary>
    public int MaxCharactersPerAccount { get; init; } = 5;

    /// <summary>The look of a character created without one.</summary>
    public string DefaultFigure { get; init; } = "hd-180-1.ch-210-66.lg-270-82.sh-290-80";
}
