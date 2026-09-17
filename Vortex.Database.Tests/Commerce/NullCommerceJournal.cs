using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Commerce;

namespace Vortex.Database.Tests.Commerce;

/// <summary>
/// A journal for the suites that are about something else. It answers every step as fresh, so a flow
/// under test behaves exactly as it would on its first attempt — which is what a test of the flow
/// itself wants. Idempotence has its own suites.
/// </summary>
/// <remarks>
/// It does keep a note of what it was asked to record. Not to assert on the journal's behaviour,
/// which belongs on the real one over a real schema, but to answer the one question a fake can
/// answer honestly: whether the flow journalled at all. Three flows were found moving money without
/// ever opening an operation, and a double that discarded everything is why nothing noticed.
/// </remarks>
internal sealed class NullCommerceJournal : ICommerceJournal
{
    public List<(CommerceOperationId Id, CommerceOperationKind Kind)> Opened { get; } = [];

    public List<(
        CommerceOperationId Id,
        CommerceOperationState State,
        string? Step
    )> Transitions { get; } = [];

    public Task OpenAsync(
        CommerceOperationId id,
        CommerceOperationKind kind,
        int playerId,
        string? detail,
        CancellationToken ct
    )
    {
        Opened.Add((id, kind));

        return Task.CompletedTask;
    }

    public Task OpenIfNewAsync(
        CommerceOperationId id,
        CommerceOperationKind kind,
        int playerId,
        string? detail,
        CancellationToken ct
    ) => Task.CompletedTask;

    public Task TransitionAsync(
        CommerceOperationId id,
        CommerceOperationState state,
        string? step,
        string? error,
        CancellationToken ct
    )
    {
        Transitions.Add((id, state, step));

        return Task.CompletedTask;
    }

    public Task<bool> TryRecordStepAsync(
        CommerceOperationId id,
        string stepKey,
        string? result,
        CancellationToken ct
    ) => Task.FromResult(true);

    public Task CompleteWithRelayAsync(
        CommerceOperationId id,
        Vortex.Primitives.Events.IEvent criticalEvent,
        CancellationToken ct
    ) => Task.CompletedTask;

    public Task<IReadOnlyList<CommerceRelayEntry>> GetUnrelayedAsync(
        int limit,
        CancellationToken ct
    ) => Task.FromResult<IReadOnlyList<CommerceRelayEntry>>([]);

    public Task MarkRelayedAsync(CommerceOperationId id, CancellationToken ct) =>
        Task.CompletedTask;

    public Task<string?> GetStepResultAsync(
        CommerceOperationId id,
        string stepKey,
        CancellationToken ct
    ) => Task.FromResult<string?>(null);

    public Task<IReadOnlyList<CommerceOperationRecord>> GetIncompletePivotedAsync(
        int limit,
        CancellationToken ct
    ) => Task.FromResult<IReadOnlyList<CommerceOperationRecord>>([]);

    public Task<IReadOnlyList<CommerceOperationRecord>> GetEscalatableAsync(
        DateTime pivotedBefore,
        int limit,
        CancellationToken ct
    ) => Task.FromResult<IReadOnlyList<CommerceOperationRecord>>([]);
}
