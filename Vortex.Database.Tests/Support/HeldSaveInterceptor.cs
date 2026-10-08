using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Vortex.Database.Tests.Support;

/// <summary>
/// Holds every save until <see cref="Release"/>, as a slow database round trip would. In-memory
/// SQLite completes every call synchronously, so two calls started together otherwise run one after
/// the other and a race between a check and its write can never show.
/// </summary>
internal sealed class HeldSaveInterceptor : SaveChangesInterceptor
{
    private readonly TaskCompletionSource _release = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    public void Release() => _release.TrySetResult();

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken ct = default
    )
    {
        await _release.Task;

        return result;
    }
}
