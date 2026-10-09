using Orleans;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Skills;

/// <summary>One status a fighter carries.</summary>
[GenerateSerializer, Immutable]
public sealed record RpgStatusEntry
{
    [Id(0)]
    public required string StatusId { get; init; }

    [Id(1)]
    public required int RemainingMs { get; init; }

    [Id(2)]
    public required int Stacks { get; init; }
}
