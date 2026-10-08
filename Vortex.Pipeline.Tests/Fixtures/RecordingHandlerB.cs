using System.Threading;
using System.Threading.Tasks;
using Vortex.Pipeline.Registry;

namespace Vortex.Pipeline.Tests.Fixtures;

public sealed class RecordingHandlerB(Recorder recorder) : IHandler<TestEnvelope, TestContext>
{
    public ValueTask HandleAsync(TestEnvelope env, TestContext ctx, CancellationToken ct)
    {
        recorder.Record($"B:{env.Payload}");

        return ValueTask.CompletedTask;
    }
}
