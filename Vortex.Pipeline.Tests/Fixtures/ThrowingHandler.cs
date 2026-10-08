using System.Threading;
using System.Threading.Tasks;
using Vortex.Pipeline.Registry;

namespace Vortex.Pipeline.Tests.Fixtures;

public sealed class ThrowingHandler : IHandler<TestEnvelope, TestContext>
{
    public sealed class BoomException(string message) : System.Exception(message);

    public ValueTask HandleAsync(TestEnvelope env, TestContext ctx, CancellationToken ct) =>
        throw new BoomException("handler blew up");
}
