using System;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Pipeline.Attributes;
using Vortex.Pipeline.Registry;

namespace Vortex.Pipeline.Tests.Fixtures;

[Order(-5)]
public sealed class HighPriorityBehavior(Recorder recorder) : IBehavior<TestEnvelope, TestContext>
{
    public async ValueTask InvokeAsync(
        TestEnvelope env,
        TestContext ctx,
        Func<ValueTask> next,
        CancellationToken ct
    )
    {
        recorder.Record("Behavior(-5):before");
        await next().ConfigureAwait(true);
        recorder.Record("Behavior(-5):after");
    }
}
