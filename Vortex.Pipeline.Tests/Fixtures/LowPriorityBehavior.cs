using System;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Pipeline.Attributes;
using Vortex.Pipeline.Registry;

namespace Vortex.Pipeline.Tests.Fixtures;

[Order(10)]
public sealed class LowPriorityBehavior(Recorder recorder) : IBehavior<TestEnvelope, TestContext>
{
    public async ValueTask InvokeAsync(
        TestEnvelope env,
        TestContext ctx,
        Func<ValueTask> next,
        CancellationToken ct
    )
    {
        recorder.Record("Behavior(10):before");
        await next().ConfigureAwait(true);
        recorder.Record("Behavior(10):after");
    }
}
