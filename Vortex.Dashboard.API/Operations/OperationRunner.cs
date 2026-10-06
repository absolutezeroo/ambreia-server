using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Dashboard.API.Security;
using Vortex.Database.Auditing;
using Vortex.Observability.Diagnostics;
using Vortex.Primitives.Action;
using Vortex.Primitives.Observability;

namespace Vortex.Dashboard.API.Operations;

/// <summary>
/// What every dashboard write goes through, whatever it writes.
/// </summary>
/// <remarks>
/// <para>
/// One correlation id shared with the HTTP access row and the error the operator is holding, a trace
/// scope, a capture of what the domain call changed so the audit says what a delete erased rather
/// than which id it was pointed at, one timestamp for all three exits, and the metric.
/// </para>
/// <para>
/// A collaborator rather than a base class, and emphatically not something each subject copies:
/// it was a private method on a service with thirty-two constructor dependencies, and splitting
/// that service per subject would otherwise have turned one subtle mechanism into thirty
/// divergent ones. Its own four dependencies are the whole of what auditing a write needs.
/// </para>
/// </remarks>
internal sealed partial class OperationRunner(
    IAuditSink auditSink,
    IVortexContextAccessor context,
    IVortexMetrics metrics,
    ILogger<OperationRunner> logger
)
{
    private readonly IAuditSink _auditSink = auditSink;
    private readonly IVortexContextAccessor _context = context;
    private readonly IVortexMetrics _metrics = metrics;
    private readonly ILogger<OperationRunner> _logger = logger;

    public async Task<OperationResult> ExecuteAsync(
        string action,
        string actor,
        string reason,
        long? targetPlayerId,
        int? roomId,
        object detail,
        Func<CancellationToken, Task> work,
        CancellationToken ct,
        AuditCategory category = AuditCategory.Staff
    )
    {
        // The request's id when there is one, so the operation's audit row, the HTTP access row and
        // the error the operator is holding all name the same thing. A fresh id here meant three ids
        // for one failed click.
        CorrelationId correlationId = _context.Current?.CorrelationId ?? CorrelationId.New();

        using IVortexTraceScope scope = _context.BeginScope(
            action,
            correlationId: correlationId,
            playerId: targetPlayerId,
            roomId: roomId
        );

        // Armed for the duration of the domain call so the audit can say what the write replaced,
        // not merely which id it was pointed at. Before this, a delete recorded `{ offerId: 12 }`
        // and the row itself was gone -- there was nowhere left to read what it had been.
        using IEntityChangeCapture capture = EntityChangeCapture.Begin();

        // One timestamp for all three exits. The audit row says what happened and the trail is
        // queryable, but a table is not an alert: an action that has started failing, or one that has
        // quietly gone from 40ms to four seconds, is invisible until somebody thinks to look.
        long startedAt = Stopwatch.GetTimestamp();

        try
        {
            await work(ct).ConfigureAwait(false);

            Measure(action, "success", startedAt);

            Emit(
                action,
                AuditResult.Success,
                AuditSeverity.Notice,
                correlationId,
                actor,
                reason,
                targetPlayerId,
                roomId,
                detail,
                category,
                capture.Changes
            );

            return OperationResult.Succeeded(correlationId.Value);
        }
        catch (InvalidOperationException ex) when (IsDomainCode(ex.Message))
        {
            // Expected domain-validation rejection (e.g. duplicate catalog code) rather than an
            // infrastructure fault — logged at a lower severity and the reason is surfaced to the
            // operator instead of the generic "operation_failed".
            //
            // Guarded by the shape of the message, because InvalidOperationException is not only the
            // domain's: EF throws it too ("Sequence contains no elements", "The instance of entity
            // type cannot be tracked..."), and this branch put whatever it said on the operator's
            // screen. One filtered `when` sends those to the fault branch below instead, which logs
            // the exception and answers a generic code -- no schema, no query, no stack.
            Measure(action, "rejected", startedAt);

            _logger.LogInformation(
                VortexEventIds.DashboardFault,
                "Dashboard operation {Action} rejected: {Reason}",
                action,
                ex.Message
            );

            Emit(
                action,
                AuditResult.Failed,
                AuditSeverity.Notice,
                correlationId,
                actor,
                reason,
                targetPlayerId,
                roomId,
                detail,
                category,
                // Usually empty -- a domain rejection happens before anything is written. When it is
                // not, a row was saved before the refusal, and that is exactly the case worth seeing.
                capture.Changes
            );

            return OperationResult.Failed(correlationId.Value, ex.Message);
        }
        catch (Exception ex)
        {
            Measure(action, "failed", startedAt);

            _logger.LogError(
                VortexEventIds.DashboardFault,
                ex,
                "Dashboard operation {Action} failed",
                action
            );

            Emit(
                action,
                AuditResult.Failed,
                AuditSeverity.Warning,
                correlationId,
                actor,
                reason,
                targetPlayerId,
                roomId,
                detail,
                category,
                capture.Changes
            );

            return OperationResult.Failed(correlationId.Value);
        }
    }

    /// <summary>
    ///     Whether an exception message is one of the domain's own rejection codes rather than a
    ///     framework sentence. Every deliberate one is lowercase snake_case — <c>offer_has_products</c>,
    ///     <c>account_not_found</c> — and nothing thrown by EF, Orleans or the BCL looks like that.
    /// </summary>
    internal static bool IsDomainCode(string? message) =>
        !string.IsNullOrEmpty(message)
        && message.Length <= 64
        && DomainCodePattern().IsMatch(message);

    [GeneratedRegex("^[a-z][a-z0-9_]*$")]
    private static partial Regex DomainCodePattern();

    private void Measure(string action, string outcome, long startedAt) =>
        _metrics.DashboardOperationCompleted(
            action,
            outcome,
            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds
        );

    /// <summary>
    ///     Whether the name an operation was given differs from the operator the request arrived as.
    /// </summary>
    /// <remarks>
    ///     Null outside a request — a console command or a background sweep has no session to compare
    ///     against, and "no opinion" is the honest value there rather than false. Null too when they
    ///     agree, so the field only appears in the rows worth looking at.
    /// </remarks>
    private static bool? Mismatched(string actor)
    {
        ActorSecurityContext? current = ActorSecurityContext.Current;

        if (current is null)
        {
            return null;
        }

        return string.Equals(actor, current.Email, StringComparison.OrdinalIgnoreCase)
            ? null
            : true;
    }

    private void Emit(
        string action,
        AuditResult result,
        AuditSeverity severity,
        CorrelationId correlationId,
        string actor,
        string reason,
        long? targetPlayerId,
        int? roomId,
        object detail,
        AuditCategory category = AuditCategory.Staff,
        IReadOnlyList<EntityChange>? changes = null
    ) =>
        _auditSink.Emit(
            new AuditEvent
            {
                Category = category,
                Action = action,
                Severity = severity,
                Result = result,
                CorrelationId = correlationId,
                TargetPlayerId = targetPlayerId,
                RoomId = roomId,
                Data = JsonSerializer.Serialize(
                    new
                    {
                        actor,
                        reason,
                        detail,
                        // The server's own view of who asked, next to the name the caller passed.
                        // `actor` is an argument -- every operation forwards one and nothing checks
                        // it -- so an audit trail built on it alone records what it was told. This
                        // records the account behind the session the request actually arrived on,
                        // and `actorMismatch` is the case worth finding: a row where the two
                        // disagree is either a bug in a call site or somebody writing under a name
                        // that is not theirs.
                        actorAccountId = ActorSecurityContext.Current?.AccountId,
                        actorMismatch = Mismatched(actor),
                        // Omitted rather than written as an empty array: most operations touch no
                        // tracked row (they call a grain, or use a bulk statement), and a `changes: []`
                        // on every one of those would read as "nothing changed" instead of "not
                        // recorded here".
                        changes = changes is { Count: > 0 } ? changes : null,
                    }
                ),
            }
        );
}
