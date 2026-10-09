using System.Collections.Immutable;
using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Skills;

/// <summary>What skills and statuses did this tick (spec M1b §10.2, 9024), to the zone. The basic
/// attack stays in <c>RpgCombatEvents</c>.</summary>
[GenerateSerializer, Immutable]
public sealed record RpgSkillEventsMessageComposer : IComposer
{
    [Id(0)]
    public required ImmutableArray<RpgSkillImpact> Impacts { get; init; }

    [Id(1)]
    public required ImmutableArray<RpgSkillOutcome> Outcomes { get; init; }
}
