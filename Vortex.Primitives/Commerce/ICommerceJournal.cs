using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Primitives.Commerce;

/// <summary>
/// The durable record of every value-moving operation in flight, and of every post-pivot step that
/// has already been applied.
/// </summary>
/// <remarks>
/// <para>
/// A table rather than a grain per operation. Recovery and operations both need to <em>query</em>
/// this — "what is stuck past its pivot", "what needs intervention" — and a grain per operation
/// gives an activation per row and no way to ask that question at all.
/// </para>
/// <para>
/// It is also the outbox. Every critical business event identified so far belongs to an operation,
/// so the operation's terminal transition is where the relay reads from: one durable write per
/// transition, one at-least-once relay, consumers idempotent by operation id. A second, independent
/// outbox table would be a second thing to keep consistent for no event that needs it.
/// </para>
/// </remarks>
public interface ICommerceJournal
{
    /// <summary>
    /// Opens an operation in <see cref="CommerceOperationState.Prepared"/>. Called after preflight
    /// and before anything durable happens, so that a crash one instruction later is still
    /// recoverable.
    /// </summary>
    Task OpenAsync(
        CommerceOperationId id,
        CommerceOperationKind kind,
        int playerId,
        string? detail,
        CancellationToken ct
    );

    /// <summary>
    /// Opens the operation unless it is already open. For flows whose id is derived from the entity
    /// they act on (see <see cref="CommerceOperationId.Deterministic"/>), where a second attempt is
    /// the same operation rather than a new one.
    /// </summary>
    Task OpenIfNewAsync(
        CommerceOperationId id,
        CommerceOperationKind kind,
        int playerId,
        string? detail,
        CancellationToken ct
    );

    /// <summary>
    /// Moves the operation to a new state. Passing <see cref="CommerceOperationState.Pivoted"/>
    /// stamps the pivot time, which is what the "stuck past its pivot" alert reads.
    /// </summary>
    Task TransitionAsync(
        CommerceOperationId id,
        CommerceOperationState state,
        string? step,
        string? error,
        CancellationToken ct
    );

    /// <summary>
    /// Records that a step has been applied, and returns whether this call is the one that recorded
    /// it. A false return means the step ran before — the caller must skip the work and treat the
    /// operation as having already had it done.
    /// </summary>
    /// <remarks>
    /// This is the whole of idempotence. Every post-pivot step is either covered by one of these or
    /// naturally idempotent, and which one it is gets proven by a replay test rather than assumed —
    /// <c>PlayerEffectGrain.AddEffectAsync</c> inserted unconditionally for as long as it existed,
    /// so a retried grant simply gave the effect twice.
    /// </remarks>
    Task<bool> TryRecordStepAsync(
        CommerceOperationId id,
        string stepKey,
        string? result,
        CancellationToken ct
    );

    /// <summary>
    /// Completes the operation and, in the same write, records the critical business event it owes.
    /// </summary>
    /// <remarks>
    /// A purchase used to publish its event immediately after succeeding, outside any transaction: a
    /// crash between the commit and the publish lost the event, and with it the quest progress and
    /// the daily task it feeds. The event is written with the transition and relayed afterwards —
    /// at-least-once, which is why the consumers that matter deduplicate by operation id.
    /// </remarks>
    Task CompleteWithRelayAsync(
        CommerceOperationId id,
        Events.IEvent criticalEvent,
        CancellationToken ct
    );

    /// <summary>Events written with a terminal transition and not yet published.</summary>
    Task<IReadOnlyList<CommerceRelayEntry>> GetUnrelayedAsync(int limit, CancellationToken ct);

    /// <summary>Marks a relayed event as published, so it is not published again.</summary>
    Task MarkRelayedAsync(CommerceOperationId id, CancellationToken ct);

    /// <summary>The result recorded for a step, or null if it has not run.</summary>
    Task<string?> GetStepResultAsync(CommerceOperationId id, string stepKey, CancellationToken ct);

    /// <summary>
    /// Operations that pivoted and never completed. The recovery owner's work list, and what an
    /// operator is shown when the "stuck post-pivot" alert fires.
    /// </summary>
    Task<IReadOnlyList<CommerceOperationRecord>> GetIncompletePivotedAsync(
        int limit,
        CancellationToken ct
    );

    /// <summary>
    /// The operations the sweep can still act on: past their pivot, not escalated yet, and stuck
    /// since before <paramref name="pivotedBefore"/>.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="GetIncompletePivotedAsync"/>, which is the operator's list and
    /// deliberately includes NeedsIntervention. The sweep used to read that one and skip the
    /// interventions inside its loop — after the <c>Take</c> had already spent the batch on them.
    /// One full batch of old interventions, a hundred by default, and no newer stuck operation was
    /// ever examined again: the journal still had them, the automatic detection just stopped
    /// moving. Both filters belong in SQL, ahead of the limit.
    /// </remarks>
    Task<IReadOnlyList<CommerceOperationRecord>> GetEscalatableAsync(
        System.DateTime pivotedBefore,
        int limit,
        CancellationToken ct
    );
}

/// <summary>One row of the journal, as ops and recovery read it.</summary>
public sealed record CommerceOperationRecord
{
    public required CommerceOperationId Id { get; init; }
    public required CommerceOperationKind Kind { get; init; }
    public required int PlayerId { get; init; }
    public required CommerceOperationState State { get; init; }
    public required int Attempts { get; init; }
    public string? CurrentStep { get; init; }
    public string? LastError { get; init; }
    public string? Detail { get; init; }
    public System.DateTime? PivotedAt { get; init; }
    public System.DateTime CreatedAt { get; init; }
}

/// <summary>One event owed by a completed operation.</summary>
public sealed record CommerceRelayEntry
{
    public required CommerceOperationId OperationId { get; init; }
    public required string TypeName { get; init; }
    public required string Payload { get; init; }
}
