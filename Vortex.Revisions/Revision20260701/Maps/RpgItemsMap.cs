using Vortex.Primitives.Networking.Revisions;
using Vortex.Protocol.Messages.Outgoing.Rpg.Items;
using Vortex.Revisions.Revision20260701.Parsers.Rpg.Items;
using Vortex.Revisions.Revision20260701.Serializers.Rpg.Items;

namespace Vortex.Revisions.Revision20260701.Maps;

/// <summary>The bag and the equipment (spec Objets §17).</summary>
internal sealed class RpgItemsMap : IRevisionMap
{
    public void RegisterInto(IRevisionMapBuilder builder)
    {
        builder.MapParser(MessageEvent.RpgItemMoveMessageEvent, new RpgItemMoveMessageParser());
        builder.MapParser(MessageEvent.RpgItemSplitMessageEvent, new RpgItemSplitMessageParser());
        builder.MapParser(MessageEvent.RpgItemEquipMessageEvent, new RpgItemEquipMessageParser());
        builder.MapParser(
            MessageEvent.RpgItemUnequipMessageEvent,
            new RpgItemUnequipMessageParser()
        );
        builder.MapParser(
            MessageEvent.RpgItemDestroyMessageEvent,
            new RpgItemDestroyMessageParser()
        );
        builder.MapParser(
            MessageEvent.RpgItemClaimParcelMessageEvent,
            new RpgItemClaimParcelMessageParser()
        );

        builder.MapSerializer(
            typeof(RpgInventoryMessageComposer),
            new RpgInventoryMessageComposerSerializer(MessageComposer.RpgInventoryMessageComposer)
        );
        builder.MapSerializer(
            typeof(RpgItemResultMessageComposer),
            new RpgItemResultMessageComposerSerializer(MessageComposer.RpgItemResultMessageComposer)
        );
    }
}
