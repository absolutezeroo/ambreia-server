using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Nft;

namespace Vortex.Revisions.Revision20260701.Serializers.Nft;

internal class UserPurchasableChatStyleChangedMessageComposerSerializer(int header)
    : AbstractSerializer<UserPurchasableChatStyleChangedMessageComposer>(header)
{
    protected override void Serialize(
        IServerPacket packet,
        UserPurchasableChatStyleChangedMessageComposer message
    )
    {
        // The flag comes FIRST. The parser reads added before styleId, and the getters are declared
        // in that order too — but the field order is the constructor's, not the getters', and this
        // is one of the few places in the domain where both agree.
        packet.WriteBoolean(message.Added).WriteInteger(message.ChatStyleId);
    }
}
