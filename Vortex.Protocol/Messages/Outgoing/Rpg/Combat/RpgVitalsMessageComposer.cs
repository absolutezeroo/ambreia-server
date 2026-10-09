using System.Collections.Immutable;
using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Combat;

/// <summary>Combatants whose health changed without a hit — regeneration, birth, respawn, a monster
/// back home (spec M1a §9.2, 9013); every combatant in the zone's entry burst.</summary>
[GenerateSerializer, Immutable]
public sealed record RpgVitalsMessageComposer : IComposer
{
    [Id(0)]
    public required ImmutableArray<RpgCombatantVitals> Combatants { get; init; }
}
