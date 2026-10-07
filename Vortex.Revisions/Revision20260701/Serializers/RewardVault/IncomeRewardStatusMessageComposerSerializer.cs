using Vortex.Primitives.Orleans.Snapshots.RewardVault;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.RewardVault;

namespace Vortex.Revisions.Revision20260701.Serializers.RewardVault;

internal class IncomeRewardStatusMessageComposerSerializer(int header)
    : AbstractSerializer<IncomeRewardStatusMessageComposer>(header)
{
    protected override void Serialize(
        IServerPacket packet,
        IncomeRewardStatusMessageComposer message
    )
    {
        packet.WriteInteger(message.IncomeRewards.Count);

        foreach (IncomeRewardSnapshot reward in message.IncomeRewards)
        {
            packet
                .WriteByte(unchecked((byte)(sbyte)reward.RewardCategory))
                .WriteByte((byte)reward.RewardType)
                .WriteInteger(reward.Amount)
                .WriteString(reward.ProductCode);
        }
    }
}
