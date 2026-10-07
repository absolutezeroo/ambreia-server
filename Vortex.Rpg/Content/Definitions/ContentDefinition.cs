namespace Vortex.Rpg.Content.Definitions;

/// <summary>What every content document carries: its stable id, its kind, and whether it is retired.</summary>
public abstract record ContentDefinition
{
    /// <summary>The stable id (spec Contenu §4.1), its first segment being <see cref="Kind"/>.</summary>
    public required string Id { get; init; }

    public required string Kind { get; init; }

    /// <summary>No longer obtainable, still loaded for those who hold it (spec Contenu §8).</summary>
    public bool Retired { get; init; }
}
