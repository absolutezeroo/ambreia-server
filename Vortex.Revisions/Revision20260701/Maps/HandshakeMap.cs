using Vortex.Primitives.Networking.Revisions;
using Vortex.Protocol.Messages.Outgoing.Handshake;
using Vortex.Revisions.Revision20260701.Parsers.Handshake;
using Vortex.Revisions.Revision20260701.Serializers.Handshake;

namespace Vortex.Revisions.Revision20260701.Maps;

internal sealed class HandshakeMap : IRevisionMap
{
    public void RegisterInto(IRevisionMapBuilder builder)
    {
        builder.MapParser(MessageEvent.ClientHelloMessageEvent, new ClientHelloMessageParser());
        builder.MapParser(
            MessageEvent.CompleteDiffieHandshakeMessageEvent,
            new CompleteDiffieHandshakeMessageParser()
        );
        builder.MapParser(MessageEvent.DisconnectMessageEvent, new DisconnectMessageParser());
        builder.MapParser(MessageEvent.InfoRetrieveMessageEvent, new InfoRetrieveMessageParser());
        builder.MapParser(
            MessageEvent.InitDiffieHandshakeMessageEvent,
            new InitDiffieHandshakeMessageParser()
        );
        builder.MapParser(MessageEvent.PongMessageEvent, new PongMessageParser());
        builder.MapParser(MessageEvent.SSOTicketMessageEvent, new SSOTicketMessageParser());
        builder.MapParser(MessageEvent.UniqueIDMessageEvent, new UniqueIdMessageParser());
        builder.MapParser(MessageEvent.VersionCheckMessageEvent, new VersionCheckMessageParser());

        builder.MapSerializer(
            typeof(AuthenticationOKMessage),
            new AuthenticationOKMessageSerializer(MessageComposer.AuthenticationOKMessageComposer)
        );
        builder.MapSerializer(
            typeof(CompleteDiffieHandshakeMessageComposer),
            new CompleteDiffieHandshakeMessageSerializer(
                MessageComposer.CompleteDiffieHandshakeComposer
            )
        );
        builder.MapSerializer(
            typeof(DisconnectReasonEventMessageComposer),
            new DisconnectReasonEventMessageComposerSerializer(
                MessageComposer.DisconnectReasonComposer
            )
        );
        builder.MapSerializer(
            typeof(InitDiffieHandshakeMessageComposer),
            new InitDiffieHandshakeMessageSerializer(MessageComposer.InitDiffieHandshakeComposer)
        );
        builder.MapSerializer(
            typeof(PingMessage),
            new PingMessageSerializer(MessageComposer.PingMessageComposer)
        );
        builder.MapSerializer(
            typeof(UserObjectMessage),
            new UserObjectMessageSerializer(MessageComposer.UserObjectComposer)
        );
        builder.MapSerializer(
            typeof(UserRightsMessage),
            new UserRightsMessageSerializer(MessageComposer.UserRightsMessageComposer)
        );
    }
}
