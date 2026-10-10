using System;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Pipeline.Registry;

namespace Vortex.Pipeline.Tests.Fixtures;

/// <summary>A gate that blows up before deciding: what a fail-closed pipeline must treat as a refusal.</summary>
public sealed class ThrowingBehavior : IBehavior<TestEnvelope, TestContext>
{
    public sealed class BoomException(string message) : Exception(message);

    public ValueTask InvokeAsync(
        TestEnvelope env,
        TestContext ctx,
        Func<ValueTask> next,
        CancellationToken ct
    ) => throw new BoomException("behavior blew up");
}
