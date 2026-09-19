using Orleans;

namespace Vortex.Primitives.Moderation;

/// <summary>
/// One report the player filed, as their own "my reports" window lists it back to them — open ones
/// included, unlike <see cref="CfhPendingCallSnapshot"/>, which is only the ones still withdrawable.
/// </summary>
/// <remarks>
/// The times are epoch milliseconds because the client feeds them straight to <c>new Date(...)</c>.
/// <see cref="CloseTime"/> is -1 rather than 0 while the report is open: the client tests that exact
/// value to decide between "pending" and "decided", so a 0 would show the player a decision dated
/// 1970 on a report nobody has looked at yet.
/// </remarks>
[GenerateSerializer, Immutable]
public sealed record CfhReportStatusSnapshot
{
    [Id(0)]
    public required int Id { get; init; }

    [Id(1)]
    public required long CreationTime { get; init; }

    [Id(2)]
    public required string Message { get; init; }

    /// <summary>The topic picked when reporting. The client localizes it as
    /// <c>help.cfh.topic.&lt;id&gt;</c>, so this is the topic id, not the category's.</summary>
    [Id(3)]
    public required int TopicId { get; init; }

    /// <summary>Empty for a room report, which names a room and nobody — the client prints its own
    /// "Deleted" placeholder in that case rather than a blank cell.</summary>
    [Id(4)]
    public required string ReportedAccountName { get; init; }

    /// <summary>-1 while the report is still open.</summary>
    [Id(5)]
    public required long CloseTime { get; init; }

    [Id(6)]
    public required bool Sanctioned { get; init; }

    [Id(7)]
    public required CfhAppealStatus AppealStatus { get; init; }

    /// <summary>-1 while no appeal was filed, for the same reason <see cref="CloseTime"/> is.</summary>
    [Id(8)]
    public required long AppealCreationTime { get; init; }

    /// <summary>-1 while the appeal is still pending; the client prints "-" on that exact value.</summary>
    [Id(9)]
    public required long AppealResolutionTime { get; init; }
}
