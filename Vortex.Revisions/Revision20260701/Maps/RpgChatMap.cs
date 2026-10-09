using Vortex.Primitives.Networking.Revisions;
using Vortex.Protocol.Messages.Outgoing.Rpg.Chat;
using Vortex.Revisions.Revision20260701.Parsers.Rpg.Chat;
using Vortex.Revisions.Revision20260701.Serializers.Rpg.Chat;

namespace Vortex.Revisions.Revision20260701.Maps;

/// <summary>The chat (spec F3c §10).</summary>
internal sealed class RpgChatMap : IRevisionMap
{
    public void RegisterInto(IRevisionMapBuilder builder)
    {
        builder.MapParser(MessageEvent.RpgChatSendMessageEvent, new RpgChatSendMessageParser());

        builder.MapSerializer(
            typeof(RpgChatMessageComposer),
            new RpgChatMessageComposerSerializer(MessageComposer.RpgChatMessageComposer)
        );
        builder.MapSerializer(
            typeof(RpgChatResultMessageComposer),
            new RpgChatResultMessageComposerSerializer(MessageComposer.RpgChatResultMessageComposer)
        );
    }
}
