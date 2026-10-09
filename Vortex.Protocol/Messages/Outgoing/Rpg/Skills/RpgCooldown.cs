using Orleans;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Skills;

/// <summary>A skill still recharging.</summary>
[GenerateSerializer, Immutable]
public sealed record RpgCooldown
{
    [Id(0)]
    public required string SkillId { get; init; }

    [Id(1)]
    public required int RemainingMs { get; init; }
}
