namespace Vortex.Rpg.Characters.Configuration;

/// <summary>The bag, the parcels and how often moved items are written (spec Objets §2, §6).</summary>
public sealed class ItemConfig
{
    public const string SECTION_NAME = "Vortex:Items";

    /// <summary>How often moved, split and equipped items are written; a grant, a destroy and a
    /// removal are written at once.</summary>
    public int FlushIntervalMs { get; init; } = 2_000;

    public int BagSize { get; init; } = 30;

    /// <summary>Parcels kept for a character whose bag was full; past this a grant is refused.</summary>
    public int MaxParcels { get; init; } = 50;
}
