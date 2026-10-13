using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Rpg.Combat;

namespace Vortex.Revisions.Revision20260701.Serializers.Rpg.Combat;

internal class RpgTargetsMessageComposerSerializer(int header) : AbstractSerializer<RpgTargetsMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, RpgTargetsMessageComposer message)
    {
        packet.WriteInteger(message.Entries.Length);

        foreach (RpgTargetEntry entry in message.Entries)
        {
            packet.WriteInteger(entry.ObjectId).WriteInteger(entry.TargetObjectId);
        }
    }
}
