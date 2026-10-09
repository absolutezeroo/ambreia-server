using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vortex.Events.Registry;
using Vortex.Players.Chat;
using Vortex.Players.Chat.Rules;
using Vortex.Players.Configuration;
using Vortex.Primitives.Chat;
using Vortex.Primitives.Events;
using Vortex.Primitives.Observability;
using Vortex.Tests.Support;

namespace Vortex.Players.Tests.Support;

/// <summary>A real chat pipeline for presence tests: these channels, the shipped rules, no link
/// kind, events that change nothing.</summary>
internal static class TestChat
{
    public static ChatPipeline Pipeline(params IChatChannel[] channels)
    {
        IOptions<ChatConfig> config = Options.Create(new ChatConfig());

        return new ChatPipeline(
            channels,
            [
                new ChatLengthRule(config),
                new ChatRateRule(config, NullLogger<ChatRateRule>.Instance),
            ],
            [],
            config,
            FakeProxy.Create<ICancellableEventPublisher>(_ =>
                Task.FromResult(new EventContext { CorrelationId = "test" })
            ),
            FakeProxy.Create<IEventPublisher>(_ => Task.CompletedTask),
            FakeProxy.Create<IVortexMetrics>(_ => null),
            NullLogger<ChatPipeline>.Instance
        );
    }
}
