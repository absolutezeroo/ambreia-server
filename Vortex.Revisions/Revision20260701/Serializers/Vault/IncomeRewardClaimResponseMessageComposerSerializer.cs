using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Vault;

namespace Vortex.Revisions.Revision20260701.Serializers.Vault;

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
