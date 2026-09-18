using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Nft;

namespace Vortex.Revisions.Revision20260701.Serializers.Nft;

internal class UserPurchasableChatStylesMessageComposerSerializer(int header)
    : AbstractSerializer<UserPurchasableChatStylesMessageComposer>(header)
{
    protected override void Serialize(
        IServerPacket packet,
        UserPurchasableChatStylesMessageComposer message
    )
    {
        packet.WriteInteger(message.ChatStyleIds.Count);

        foreach (int chatStyleId in message.ChatStyleIds)
        {
            packet.WriteInteger(chatStyleId);
        }
    }
}
