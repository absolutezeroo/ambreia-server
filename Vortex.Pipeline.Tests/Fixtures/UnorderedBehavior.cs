using System;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Pipeline.Registry;

namespace Vortex.Pipeline.Tests.Fixtures;

/// <summary>A behavior with no [Order] attribute -- exercises the "defaults to 0" fallback that
/// <c>EnvelopeFeatureProcessor</c> applies via <c>GetCustomAttribute&lt;OrderAttribute&gt;()?.Value ?? 0</c>.</summary>
public sealed class UnorderedBehavior(Recorder recorder) : IBehavior<TestEnvelope, TestContext>
{
    public async ValueTask InvokeAsync(
        TestEnvelope env,
        TestContext ctx,
        Func<ValueTask> next,
        CancellationToken ct
    )
    {
        recorder.Record("Behavior(0):before");
        await next().ConfigureAwait(true);
        recorder.Record("Behavior(0):after");
    }
}
