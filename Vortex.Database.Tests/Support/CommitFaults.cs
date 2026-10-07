using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Vortex.Database.Tests.Support;

/// <summary>
/// What a real database does to a commit now and then: refuses the next <see cref="FailNext"/>
/// with a transient error, or lets it through and then runs <see cref="AfterCommit"/> (a client
/// that hangs up once the data is written).
/// </summary>
internal sealed class CommitFaults : DbTransactionInterceptor
{
    public int FailNext { get; set; }

    public Action? AfterCommit { get; set; }

    public override ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken ct = default
    )
    {
        if (FailNext > 0)
        {
            FailNext--;
            throw new TimeoutException("The commit timed out (transient).");
        }

        return ValueTask.FromResult(result);
    }

    public override Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken ct = default
    )
    {
        AfterCommit?.Invoke();

        return Task.CompletedTask;
    }
}
