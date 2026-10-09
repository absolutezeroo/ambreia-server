using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Vortex.Database.Tests.Support;

/// <summary>Fails the next <see cref="FailNext"/> saves, whatever they write — a database lost for
/// a moment. <see cref="CommitFaults"/> only sees saves that open a transaction; a one-row update
/// does not.</summary>
internal sealed class FailingSaveInterceptor : SaveChangesInterceptor
{
    public int FailNext { get; set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken ct = default
    )
    {
        if (FailNext > 0)
        {
            FailNext--;

            throw new TimeoutException("The database did not answer (transient).");
        }

        return ValueTask.FromResult(result);
    }
}
