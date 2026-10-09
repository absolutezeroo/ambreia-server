using System.Collections.Immutable;
using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Skills;

/// <summary>The statuses of the fighters whose statuses changed this tick (spec M1b §10.2, 9023), to
/// the zone; on entry, those of every fighter that carries some.</summary>
[GenerateSerializer, Immutable]
public sealed record RpgStatusesMessageComposer : IComposer
{
    [Id(0)]
    public required ImmutableArray<RpgCombatantStatuses> Combatants { get; init; }
}
