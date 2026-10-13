using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Rpg.Quests;

namespace Vortex.Revisions.Revision20260701.Serializers.Rpg.Quests;

internal class RpgDialogueCloseMessageComposerSerializer(int header)
    : AbstractSerializer<RpgDialogueCloseMessageComposer>(header)
{
    protected override void Serialize(
        IServerPacket packet,
        RpgDialogueCloseMessageComposer message
    ) { }
}
