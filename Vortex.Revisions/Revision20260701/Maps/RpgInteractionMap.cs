using Vortex.Primitives.Networking.Revisions;
using Vortex.Protocol.Messages.Outgoing.Rpg.Interaction;
using Vortex.Revisions.Revision20260701.Parsers.Rpg.Interaction;
using Vortex.Revisions.Revision20260701.Serializers.Rpg.Interaction;

namespace Vortex.Revisions.Revision20260701.Maps;

/// <summary>What a player can click in a zone, and its target (spec F3b §7).</summary>
internal sealed class RpgInteractionMap : IRevisionMap
{
    public void RegisterInto(IRevisionMapBuilder builder)
    {
        builder.MapParser(
            MessageEvent.RpgTargetSelectMessageEvent,
            new RpgTargetSelectMessageParser()
        );

        builder.MapSerializer(
            typeof(RpgZoneInteractivesMessageComposer),
            new RpgZoneInteractivesMessageComposerSerializer(
                MessageComposer.RpgZoneInteractivesMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(RpgTargetMessageComposer),
            new RpgTargetMessageComposerSerializer(MessageComposer.RpgTargetMessageComposer)
        );
    }
}
