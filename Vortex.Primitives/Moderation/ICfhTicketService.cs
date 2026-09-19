using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Primitives.Moderation;

public interface ICfhTicketService
{
    /// <param name="reportedPlayerId">Null for a room report, which names a room and nobody.</param>
    Task<int> CreateTicketAsync(
        int topicId,
        int reporterPlayerId,
        int? reportedPlayerId,
        int? roomId,
        string message,
        IReadOnlyList<(int UserId, string Text)> evidence,
        CancellationToken ct = default
    );

    /// <summary>
    /// Claims tickets for a moderator. Ids that don't exist or aren't currently Open are not
    /// silently dropped: every requested id comes back with a verdict, because the client has a
    /// dedicated rejection path (IssuePickFailed, with retry) for the ones somebody else already
    /// holds and cannot show it without being told who holds them.
    /// </summary>
    /// <remarks>
    /// Serialize calls through <c>IModerationQueueGrain</c> rather than calling this concurrently:
    /// the read-then-write inside is only race-free because that grain's turn makes it so.
    /// </remarks>
    Task<ImmutableArray<CfhTicketPickOutcome>> PickTicketsAsync(
        IReadOnlyList<int> issueIds,
        int pickerPlayerId,
        CancellationToken ct = default
    );

    Task<ImmutableArray<CfhTicketCloseOutcome>> CloseTicketsAsync(
        IReadOnlyList<int> issueIds,
        CfhTicketCloseReason reason,
        bool sanctioned,
        CancellationToken ct = default
    );

    /// <summary>Hands picked tickets back to the queue.</summary>
    /// <returns>The ids actually released — the rest were never picked, or are already closed.</returns>
    Task<ImmutableArray<int>> ReleaseTicketsAsync(
        IReadOnlyList<int> issueIds,
        CancellationToken ct = default
    );

    Task<CfhTicketSummary?> GetTicketAsync(int issueId, CancellationToken ct = default);

    /// <summary>
    /// The queue blocks for specific tickets, in the same shape <see cref="GetOpenQueueAsync"/>
    /// produces. Feeds the per-ticket pushes that keep an already-open mod tool in sync, so it
    /// deliberately does not filter by state: a moderator watching a ticket needs to see it go to
    /// Picked as much as they need to see it appear.
    /// </summary>
    Task<ImmutableArray<CfhIssueQueueEntrySnapshot>> GetQueueEntriesAsync(
        IReadOnlyList<int> issueIds,
        CancellationToken ct = default
    );

    /// <summary>The reporter's selected evidence lines, frozen at report time — not a live
    /// re-query of the room's chatlog.</summary>
    Task<CfhTicketEvidenceSnapshot?> GetTicketEvidenceAsync(
        int issueId,
        CancellationToken ct = default
    );

    Task<CfhTopicSnapshot?> GetTopicAsync(int topicId, CancellationToken ct = default);

    Task<ImmutableArray<CfhCategorySnapshot>> GetCatalogAsync(CancellationToken ct = default);

    /// <summary>Open and Picked tickets (not Closed), most recent first — the staff mod tool's
    /// issue queue, pushed at login.</summary>
    Task<ImmutableArray<CfhIssueQueueEntrySnapshot>> GetOpenQueueAsync(
        CancellationToken ct = default
    );

    /// <summary>
    /// The reports this player filed that are still open, newest first — their own view, not the
    /// staff queue. The client asks for these before it will let them file another, so this is what
    /// stops one upset player putting the same complaint in the queue six times.
    /// </summary>
    Task<ImmutableArray<CfhPendingCallSnapshot>> GetPendingForReporterAsync(
        int reporterPlayerId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Withdraws this player's own still-open reports, which is what their client offers when it
    /// shows them the pending ones. Only their own, and only ones no moderator has picked up:
    /// a report already in a moderator's hands is that moderator's to close.
    /// </summary>
    /// <returns>The ids withdrawn, so they can be dropped from the moderators' queues too.</returns>
    Task<ImmutableArray<int>> DeletePendingForReporterAsync(
        int reporterPlayerId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Every report this player filed, newest first — what their client shows under "my reports",
    /// which is a history and not a work list: closed reports are the interesting ones there, since
    /// they are the only ones carrying an outcome.
    /// </summary>
    Task<ImmutableArray<CfhReportStatusSnapshot>> GetReportHistoryForReporterAsync(
        int reporterPlayerId,
        CancellationToken ct = default
    );

    /// <summary>
    /// The reporter contesting the closure of their own report. Every condition the client's appeal
    /// button tests is re-tested server-side, because the request carries a ticket id and nothing
    /// else.
    /// </summary>
    /// <returns>False when the ticket is not this player's, is not closed, was closed with a
    /// sanction, or has already been appealed — all four are "the button should not have been
    /// enabled", so there is nothing to tell the client beyond leaving the row as it was.</returns>
    Task<bool> AppealTicketAsync(int issueId, int reporterPlayerId, CancellationToken ct = default);

    /// <summary>Appeals nobody has looked at yet, oldest first — the staff dashboard's work list.
    /// The client's own moderation tool has no appeal screen, so this is the only place they
    /// surface.</summary>
    Task<ImmutableArray<CfhAppealSnapshot>> GetPendingAppealsAsync(CancellationToken ct = default);

    /// <summary>
    /// Settles pending appeals. <paramref name="upheld"/> true marks the report sanctioned after
    /// all; false leaves the original decision standing. Either way the appeal stops being pending,
    /// which is what takes the row off the reporter's "appealed" state.
    /// </summary>
    /// <returns>The ids actually resolved — the rest had no pending appeal.</returns>
    Task<ImmutableArray<int>> ResolveAppealsAsync(
        IReadOnlyList<int> issueIds,
        bool upheld,
        CancellationToken ct = default
    );

    /// <summary>
    /// This player's own sanction history, newest first — what their client shows under "my
    /// sanctions". Bans that have already expired are included: it is a record, not a list of what
    /// is currently in force, and a player looking at an empty screen after serving one would read
    /// it as never having been sanctioned.
    /// </summary>
    Task<ImmutableArray<PlayerSanctionSnapshot>> GetSanctionHistoryAsync(
        int playerId,
        CancellationToken ct = default
    );
}
