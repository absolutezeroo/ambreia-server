using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.RewardVault;

namespace Vortex.Revisions.Revision20260701.Serializers.RewardVault;

internal class IncomeRewardClaimResponseMessageComposerSerializer(int header)
    : AbstractSerializer<IncomeRewardClaimResponseMessageComposer>(header)
{
    protected override void Serialize(
        IServerPacket packet,
        IncomeRewardClaimResponseMessageComposer message
    )
    {
        packet
            .WriteByte(unchecked((byte)(sbyte)message.RewardCategory))
            .WriteBoolean(message.Result);
    }
}
