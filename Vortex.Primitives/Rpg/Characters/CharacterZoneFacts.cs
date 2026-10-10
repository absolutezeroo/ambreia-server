using System.Collections.Immutable;
using Orleans;

namespace Vortex.Primitives.Rpg.Characters;

/// <summary>
/// What a zone knows of a character to answer its rules on every step (spec Z1 §4.3): the level and
/// what is owned. Pushed by the character on each change; the zone keeps the latest.
/// </summary>
[GenerateSerializer, Immutable]
public sealed record CharacterZoneFacts
{
    [Id(0)]
    public required int Level { get; init; }

    /// <summary>Quantity by item definition, bag and equipment; parcels are not owned yet.</summary>
    [Id(1)]
    public required ImmutableDictionary<string, int> Items { get; init; }
}
