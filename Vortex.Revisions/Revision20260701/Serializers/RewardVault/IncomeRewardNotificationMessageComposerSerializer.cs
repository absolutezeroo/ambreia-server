using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.RewardVault;

namespace Vortex.Revisions.Revision20260701.Serializers.RewardVault;

internal class IncomeRewardNotificationMessageComposerSerializer(int header)
    : AbstractSerializer<IncomeRewardNotificationMessageComposer>(header)
{
    protected override void Serialize(
        IServerPacket packet,
        IncomeRewardNotificationMessageComposer message
    )
    {
        // One byte, not an int: the client reads it with readByte, the same way it reads the
        // category on the claim response beside this one.
        packet.WriteByte((byte)message.RewardCategory);
    }
}
