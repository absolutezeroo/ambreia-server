using Orleans;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Skills;

/// <summary>A cast or channel that ended, and why (spec M1b §6.4).</summary>
[GenerateSerializer, Immutable]
public sealed record RpgCastEnd
{
    public const string Done = "done";
    public const string Cancelled = "cancelled";
    public const string Interrupted = "interrupted";

    /// <summary>Out of range, out of sight or out of mana when it was to land.</summary>
    public const string Failed = "failed";

    [Id(0)]
    public required int CasterObjectId { get; init; }

    [Id(1)]
    public required string SkillId { get; init; }

    [Id(2)]
    public required string Reason { get; init; }
}
