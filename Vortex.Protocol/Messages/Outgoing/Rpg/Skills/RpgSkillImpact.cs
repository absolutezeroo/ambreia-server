using Orleans;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Skills;

/// <summary>A skill landing, for its visuals: at a target (0: none) or a cell (−1: none).</summary>
[GenerateSerializer, Immutable]
public sealed record RpgSkillImpact
{
    [Id(0)]
    public required int CasterObjectId { get; init; }

    [Id(1)]
    public required string SkillId { get; init; }

    [Id(2)]
    public required int TargetObjectId { get; init; }

    [Id(3)]
    public required int X { get; init; }

    [Id(4)]
    public required int Y { get; init; }
}
