using Vortex.Primitives.Networking.Revisions;
using Vortex.Protocol.Messages.Outgoing.Rpg.Emote;
using Vortex.Revisions.Revision20260701.Parsers.Rpg.Emote;
using Vortex.Revisions.Revision20260701.Serializers.Rpg.Emote;

namespace Vortex.Revisions.Revision20260701.Maps;

/// <summary>RP actions of an avatar, shown to the room.</summary>
internal sealed class RpgEmoteMap : IRevisionMap
{
    public void RegisterInto(IRevisionMapBuilder builder)
    {
        builder.MapParser(MessageEvent.RpgEmoteMessageEvent, new RpgEmoteMessageParser());

        builder.MapSerializer(
            typeof(RpgEmoteMessageComposer),
            new RpgEmoteMessageComposerSerializer(MessageComposer.RpgEmoteMessageComposer)
        );
    }
}
