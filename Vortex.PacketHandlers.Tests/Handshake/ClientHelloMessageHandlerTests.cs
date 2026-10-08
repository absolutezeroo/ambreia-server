using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Messages.Registry;
using Vortex.PacketHandlers.Handshake;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Networking.Revisions;
using Vortex.Protocol.Messages.Incoming.Handshake;
using Vortex.Protocol.Messages.Outgoing.Handshake;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.PacketHandlers.Tests.Handshake;

public sealed class ClientHelloMessageHandlerTests
{
    private readonly List<object> _calls = new();

    [Fact]
    public async Task AnUnknownRevision_IsToldItsClientIsOutdated_ThenClosed()
    {
        ClientHelloMessageHandler handler = new(FakeProxy.Create<IRevisionManager>(_ => null));

        await handler.HandleAsync(
            new ClientHelloMessage
            {
                Production = "AMBREIA-201901010000",
                Platform = "FLASH20",
                ClientPlatform = 6,
                DeviceCategory = 4,
            },
            Context(),
            CancellationToken.None
        );

        _calls.Should().HaveCount(2);
        _calls[0]
            .Should()
            .BeEquivalentTo(
                new DisconnectReasonEventMessageComposer
                {
                    Reason = DisconnectReasonEventMessageComposer.IncompatibleClientVersion,
                }
            );
        _calls[1].Should().Be("close");
    }

    private MessageContext Context() =>
        new(
            FakeProxy.Create<ISessionContext>(call =>
            {
                if (call.Method.Name == nameof(ISessionContext.SendComposerAsync))
                {
                    _calls.Add(call.Args![0]!);
                }

                if (call.Method.Name == nameof(ISessionContext.CloseSessionAsync))
                {
                    _calls.Add("close");
                }

                return null;
            }),
            default,
            default
        );
}
