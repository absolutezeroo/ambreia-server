using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Primitives.Commerce;

namespace Vortex.Database.Commerce;

/// <summary>
/// Asks the receipts table whether a step really has run before.
/// </summary>
/// <remarks>
/// <para>
/// A write that inserts its step's receipt inside its own transaction finds out about a replay as a
/// <see cref="DbUpdateException"/> on the unique index over (OperationId, StepKey). Three sites used
/// to treat <em>every</em> DbUpdateException as that replay and answer success.
/// </para>
/// <para>
/// The exception says an insert failed. It never says which one. An unrelated constraint, a
/// truncated column, anything else EF wraps — each rolled the whole transaction back and was still
/// reported as "already applied", so the wallet told a purchase its debit stood when nothing had
/// committed, and the purchase went on to grant the goods for free. Absence of proof is not proof.
/// </para>
/// </remarks>
public static class CommerceReceiptLookup
{
    /// <summary>
    /// True when the step's receipt is committed — by this attempt or an earlier one. A false
    /// answer after a failed write means the failure was something else, and the caller must not
    /// pretend the step succeeded.
    /// </summary>
    public static Task<bool> HasCommittedReceiptAsync(
        this VortexDbContext dbCtx,
        CommerceOperationId operationId,
        string stepKey,
        CancellationToken ct
    ) =>
        dbCtx
            .CommerceReceipts.AsNoTracking()
            .AnyAsync(r => r.OperationId == operationId.Value && r.StepKey == stepKey, ct);
}
