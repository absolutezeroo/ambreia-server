using System.Collections.Immutable;
using Orleans;
using Vortex.Primitives.World.Rules;

namespace Vortex.Primitives.Rpg.Characters;

/// <summary>
/// The character's part of what a zone knows of a player (spec Z1 §4.3): the level and what is
/// owned. Pushed by the character on each change; the zone keeps the latest.
/// </summary>
[GenerateSerializer, Immutable]
public sealed record CharacterZoneFacts : ZoneFacts
{
    [Id(0)]
    public required int Level { get; init; }

    /// <summary>Quantity by item definition, bag and equipment; parcels are not owned yet.</summary>
    [Id(1)]
    public required ImmutableDictionary<string, int> Items { get; init; }
}
