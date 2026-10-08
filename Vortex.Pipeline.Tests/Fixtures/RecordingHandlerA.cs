using System.Threading;
using System.Threading.Tasks;
using Vortex.Pipeline.Registry;

namespace Vortex.Pipeline.Tests.Fixtures;

public sealed class RecordingHandlerA(Recorder recorder) : IHandler<TestEnvelope, TestContext>
{
    public ValueTask HandleAsync(TestEnvelope env, TestContext ctx, CancellationToken ct)
    {
        recorder.Record($"A:{env.Payload}");

        return ValueTask.CompletedTask;
    }
}
