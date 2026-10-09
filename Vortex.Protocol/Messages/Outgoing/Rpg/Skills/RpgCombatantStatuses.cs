using System.Collections.Immutable;
using Orleans;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Skills;

/// <summary>Every status a fighter carries now — the whole list, which replaces the last one.</summary>
[GenerateSerializer, Immutable]
public sealed record RpgCombatantStatuses
{
    [Id(0)]
    public required int ObjectId { get; init; }

    [Id(1)]
    public required ImmutableArray<RpgStatusEntry> Statuses { get; init; }
}
