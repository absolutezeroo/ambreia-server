using System;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Pipeline.Registry;

namespace Vortex.Pipeline.Tests.Fixtures;

/// <summary>A behavior that lets the chain run and blows up on the way back: the handlers have
/// already run once, and no policy may run them a second time.</summary>
public sealed class ThrowAfterNextBehavior : IBehavior<TestEnvelope, TestContext>
{
    public sealed class BoomException(string message) : Exception(message);

    public async ValueTask InvokeAsync(
        TestEnvelope env,
        TestContext ctx,
        Func<ValueTask> next,
        CancellationToken ct
    )
    {
        await next().ConfigureAwait(true);
        throw new BoomException("behavior blew up after next");
    }
}
