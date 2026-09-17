using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Database.Entities.Commerce;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Events;
using Vortex.Primitives.Observability;

namespace Vortex.Database.Commerce;

/// <summary>
/// The journal, on the same MySQL database as everything else it has to stay consistent with.
/// </summary>
/// <remarks>
/// Every write here is small and indexed, and none of them are on the room tick. The operation row
/// is written once per state change — five or six times over a purchase — and the receipts once per
/// post-pivot step.
/// </remarks>
public sealed class CommerceJournal(
    IDbContextFactory<VortexDbContext> dbCtxFactory,
    IVortexMetrics metrics,
    ILogger<CommerceJournal> logger
) : ICommerceJournal
{
    private readonly IDbContextFactory<VortexDbContext> _dbCtxFactory = dbCtxFactory;
    private readonly IVortexMetrics _metrics = metrics;
    private readonly ILogger<CommerceJournal> _logger = logger;

    public async Task OpenAsync(
        CommerceOperationId id,
        CommerceOperationKind kind,
        int playerId,
        string? detail,
        CancellationToken ct
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        DateTime now = DateTime.UtcNow;

        dbCtx.CommerceOperations.Add(
            new CommerceOperationEntity
            {
                Id = id.Value,
                Kind = kind,
                PlayerId = playerId,
                State = CommerceOperationState.Prepared,
                Detail = Truncate(detail, 1024),
                CreatedAt = now,
                UpdatedAt = now,
            }
        );

        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(false);

        _metrics.CommerceOperationTransitioned(kind, CommerceOperationState.Prepared);
    }

    public async Task OpenIfNewAsync(
        CommerceOperationId id,
        CommerceOperationKind kind,
        int playerId,
        string? detail,
        CancellationToken ct
    )
    {
        try
        {
            await OpenAsync(id, kind, playerId, detail, ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Already open: this is a second attempt at the same operation, which is the whole point
            // of a deterministic id.
        }
    }

    /// <summary>
    /// States a refund is never correct from. <see cref="CommerceOperationState"/> happens to order
    /// FailedBeforePivot after Pivoted, so this is a set and not a <c>&gt;=</c> comparison.
    /// </summary>
    private static bool IsPastPivot(CommerceOperationState state) =>
        state
            is CommerceOperationState.Pivoted
                or CommerceOperationState.Completing
                or CommerceOperationState.Completed
                or CommerceOperationState.NeedsIntervention;

    public async Task TransitionAsync(
        CommerceOperationId id,
        CommerceOperationState state,
        string? step,
        string? error,
        CancellationToken ct
    )
    {
        if (!IsPastPivot(state))
        {
            await TransitionCoreAsync(id, state, step, error, ct).ConfigureAwait(false);

            return;
        }

        // Past the pivot the only ways out are completion or an operator -- CommerceOperationState
        // says so on Pivoted itself. But every flow records its pivot from inside the closure the
        // wallet's purchase executor runs, and that executor compensates on ANY exception escaping
        // it. So a journal write that threw here reached back and refunded a purchase whose goods
        // had already been handed to the player: they kept the furniture and got the credits back.
        // Twelve call sites had the same shape, and the catalogue's own comment above its pivot
        // ("Nothing after it refunds") described what the code did not do.
        //
        // Recording that the pivot happened must never be able to undo the pivot. Swallowing leaves
        // the operation at its previous state with its effect applied, which is the conservative
        // end of the trade: an operator finds an unfinished operation instead of a silent loss, and
        // the amount is still in the receipts.
        try
        {
            await TransitionCoreAsync(id, state, step, error, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(
                ex,
                "Commerce operation {OperationId} applied its effect but could not be recorded as "
                    + "{State}. The effect stands; the journal does not show it.",
                id,
                state
            );
        }
    }

    private async Task TransitionCoreAsync(
        CommerceOperationId id,
        CommerceOperationState state,
        string? step,
        string? error,
        CancellationToken ct
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        CommerceOperationEntity? row = await dbCtx
            .CommerceOperations.FirstOrDefaultAsync(o => o.Id == id.Value, ct)
            .ConfigureAwait(false);

        if (row is null)
        {
            // Only reachable if the operation was never opened, which is a programming error rather
            // than a runtime condition — but losing the trail silently is exactly what this table
            // exists to prevent, so it is said out loud.
            _logger.LogError(
                "Commerce operation {OperationId} transitioned to {State} without ever being opened.",
                id,
                state
            );

            return;
        }

        row.State = state;
        row.UpdatedAt = DateTime.UtcNow;

        if (step is not null)
        {
            row.CurrentStep = Truncate(step, 64);
        }

        if (error is not null)
        {
            row.LastError = Truncate(error, 512);
            row.Attempts++;
        }

        // Stamped once. A pivot that moved would make "how long has this been stuck" meaningless.
        row.PivotedAt ??= state >= CommerceOperationState.Pivoted ? DateTime.UtcNow : null;

        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(false);

        _metrics.CommerceOperationTransitioned(row.Kind, state);
    }

    public async Task<bool> TryRecordStepAsync(
        CommerceOperationId id,
        string stepKey,
        string? result,
        CancellationToken ct
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        dbCtx.CommerceReceipts.Add(
            new CommerceReceiptEntity
            {
                OperationId = id.Value,
                StepKey = Truncate(stepKey, 64)!,
                Result = Truncate(result, 2048),
                CreatedAt = DateTime.UtcNow,
            }
        );

        try
        {
            await dbCtx.SaveChangesAsync(ct).ConfigureAwait(false);

            return true;
        }
        catch (DbUpdateException ex)
        {
            // The unique index is the mechanism: losing this insert is how a replay finds out that
            // its step already ran. It is the expected outcome of a retry, not a fault — but it is
            // also what a genuine constraint problem would look like, so it is logged at debug and
            // the caller is told to skip the work rather than repeat it.
            _logger.LogDebug(
                ex,
                "Step {StepKey} of commerce operation {OperationId} was already recorded; skipping.",
                stepKey,
                id
            );

            _metrics.CommerceStepReplayed(stepKey);

            return false;
        }
    }

    public async Task CompleteWithRelayAsync(
        CommerceOperationId id,
        IEvent criticalEvent,
        CancellationToken ct
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        CommerceOperationEntity? row = await dbCtx
            .CommerceOperations.FirstOrDefaultAsync(o => o.Id == id.Value, ct)
            .ConfigureAwait(false);

        if (row is null)
        {
            _logger.LogError(
                "Commerce operation {OperationId} completed without ever being opened.",
                id
            );

            return;
        }

        row.State = CommerceOperationState.Completed;
        row.UpdatedAt = DateTime.UtcNow;
        row.RelayType = criticalEvent.GetType().Name;
        row.RelayPayload = Truncate(
            JsonSerializer.Serialize(criticalEvent, criticalEvent.GetType()),
            4096
        );

        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(false);

        _metrics.CommerceOperationTransitioned(row.Kind, CommerceOperationState.Completed);
    }

    public async Task<IReadOnlyList<CommerceRelayEntry>> GetUnrelayedAsync(
        int limit,
        CancellationToken ct
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        return await dbCtx
            .CommerceOperations.AsNoTracking()
            .Where(o => o.RelayPayload != null && o.RelayedAt == null)
            .OrderBy(o => o.UpdatedAt)
            .Take(limit)
            .Select(o => new CommerceRelayEntry
            {
                OperationId = new CommerceOperationId(o.Id),
                TypeName = o.RelayType!,
                Payload = o.RelayPayload!,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task MarkRelayedAsync(CommerceOperationId id, CancellationToken ct)
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        await dbCtx
            .CommerceOperations.Where(o => o.Id == id.Value)
            .ExecuteUpdateAsync(up => up.SetProperty(o => o.RelayedAt, DateTime.UtcNow), ct)
            .ConfigureAwait(false);
    }

    public async Task<string?> GetStepResultAsync(
        CommerceOperationId id,
        string stepKey,
        CancellationToken ct
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        return await dbCtx
            .CommerceReceipts.AsNoTracking()
            .Where(r => r.OperationId == id.Value && r.StepKey == stepKey)
            .Select(r => r.Result)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CommerceOperationRecord>> GetIncompletePivotedAsync(
        int limit,
        CancellationToken ct
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        return await dbCtx
            .CommerceOperations.AsNoTracking()
            .Where(o =>
                o.State == CommerceOperationState.Pivoted
                || o.State == CommerceOperationState.Completing
                || o.State == CommerceOperationState.NeedsIntervention
            )
            .OrderBy(o => o.CreatedAt)
            .Take(limit)
            .Select(AsRecord)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CommerceOperationRecord>> GetEscalatableAsync(
        DateTime pivotedBefore,
        int limit,
        CancellationToken ct
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        return await dbCtx
            .CommerceOperations.AsNoTracking()
            // Both conditions in SQL, ahead of the Take. Already-escalated operations are out --
            // there is nothing left to do to them -- and so is anything still inside its grace
            // period, rather than being fetched and then skipped by the caller.
            .Where(o =>
                (
                    o.State == CommerceOperationState.Pivoted
                    || o.State == CommerceOperationState.Completing
                )
                && o.PivotedAt != null
                && o.PivotedAt <= pivotedBefore
            )
            .OrderBy(o => o.CreatedAt)
            .Take(limit)
            .Select(AsRecord)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <summary>One row as the operator and recovery read it. Shared so the two queries above
    /// cannot drift apart.</summary>
    private static readonly System.Linq.Expressions.Expression<
        Func<CommerceOperationEntity, CommerceOperationRecord>
    > AsRecord = o => new CommerceOperationRecord
    {
        Id = new CommerceOperationId(o.Id),
        Kind = o.Kind,
        PlayerId = o.PlayerId,
        State = o.State,
        Attempts = o.Attempts,
        CurrentStep = o.CurrentStep,
        LastError = o.LastError,
        Detail = o.Detail,
        PivotedAt = o.PivotedAt,
        CreatedAt = o.CreatedAt,
    };

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
