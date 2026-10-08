using System.Threading;
using System.Threading.Tasks;
using Vortex.Pipeline.Registry;

namespace Vortex.Pipeline.Tests.Fixtures;

public sealed class ConcurrencyTrackingHandler(ConcurrencyTracker tracker)
    : IHandler<TestEnvelope, TestContext>
{
    public async ValueTask HandleAsync(TestEnvelope env, TestContext ctx, CancellationToken ct)
    {
        tracker.Enter();
        try
        {
            await Task.Delay(30, ct).ConfigureAwait(true);
        }
        finally
        {
            tracker.Exit();
        }
    }
}
