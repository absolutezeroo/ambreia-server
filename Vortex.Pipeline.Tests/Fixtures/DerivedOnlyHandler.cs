using System.Threading;
using System.Threading.Tasks;
using Vortex.Pipeline.Registry;

namespace Vortex.Pipeline.Tests.Fixtures;

public sealed class DerivedOnlyHandler(Recorder recorder)
    : IHandler<DerivedTestEnvelope, TestContext>
{
    public ValueTask HandleAsync(DerivedTestEnvelope env, TestContext ctx, CancellationToken ct)
    {
        recorder.Record($"DerivedOnly:{env.Payload}");

        return ValueTask.CompletedTask;
    }
}
