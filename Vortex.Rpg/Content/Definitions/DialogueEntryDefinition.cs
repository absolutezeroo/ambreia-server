namespace Vortex.Rpg.Content.Definitions;

/// <summary>Open at <see cref="Node"/> when <see cref="Quest"/> is in <see cref="State"/> (none, active, ready, done).</summary>
public sealed record DialogueEntryDefinition
{
    public required string Quest { get; init; }

    public required string State { get; init; }

    public required string Node { get; init; }
}
