namespace Vortex.Rpg.Content.Definitions;

/// <summary>A class's specialization (spec Personnage §4); its modifiers come with the Combat spec.</summary>
public sealed record SpecializationDefinition : ContentDefinition
{
    /// <summary>The class it belongs to.</summary>
    public required string Class { get; init; }
}
