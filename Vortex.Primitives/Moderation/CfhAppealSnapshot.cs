using System;
using Orleans;

namespace Vortex.Primitives.Moderation;

/// <summary>
/// One appeal waiting for a second look, as the staff dashboard lists it. Not a wire type: the
/// client's own moderation tool has no appeal screen, so this shape answers to the dashboard only
/// and is free to carry what a moderator needs rather than what an AS3 parser reads.
/// </summary>
[GenerateSerializer, Immutable]
public sealed record CfhAppealSnapshot
{
    [Id(0)]
    public required int IssueId { get; init; }

    [Id(1)]
    public required int TopicId { get; init; }

    [Id(2)]
    public required string Message { get; init; }

    [Id(3)]
    public required int ReporterPlayerId { get; init; }

    [Id(4)]
    public required string ReporterName { get; init; }

    /// <summary>Zero for a room report, which names a room and nobody.</summary>
    [Id(5)]
    public required int ReportedPlayerId { get; init; }

    [Id(6)]
    public required string ReportedName { get; init; }

    /// <summary>When the original report was closed — the decision being appealed.</summary>
    [Id(7)]
    public required DateTime? ClosedAt { get; init; }

    [Id(8)]
    public required CfhTicketCloseReason? CloseReason { get; init; }

    [Id(9)]
    public required DateTime? AppealCreatedAt { get; init; }
}
