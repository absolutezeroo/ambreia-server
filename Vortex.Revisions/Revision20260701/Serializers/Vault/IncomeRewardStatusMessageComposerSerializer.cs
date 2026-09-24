using Vortex.Primitives.Orleans.Snapshots.Vault;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Vault;

namespace Vortex.Revisions.Revision20260701.Serializers.Vault;

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
