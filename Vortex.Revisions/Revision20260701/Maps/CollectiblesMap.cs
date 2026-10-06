using Vortex.Primitives.Networking.Revisions;
using Vortex.Protocol.Messages.Outgoing.Collectibles;
using Vortex.Protocol.Messages.Outgoing.Nft;
using Vortex.Revisions.Revision20260701.Serializers.Collectibles;
using Vortex.Revisions.Revision20260701.Serializers.Nft;

namespace Vortex.Revisions.Revision20260701.Maps;

/// <summary>
/// What is left of the collectibles protocol: the Emerald and Silver wallet balances and the chat
/// style lists, which the client files under collectibles but which belong to the wallet and the
/// avatar. The NFT, minting and store messages were removed with their feature.
/// </summary>
internal sealed class CollectiblesMap : IRevisionMap
{
    public void RegisterInto(IRevisionMapBuilder builder)
    {
        builder.MapSerializer(
            typeof(EmeraldBalanceMessageComposer),
            new EmeraldBalanceMessageComposerSerializer(
                MessageComposer.EmeraldBalanceMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(SilverBalanceMessageComposer),
            new SilverBalanceMessageComposerSerializer(MessageComposer.SilverBalanceMessageComposer)
        );
        builder.MapSerializer(
            typeof(UserNftChatStylesMessageComposer),
            new UserNftChatStylesMessageComposerSerializer(
                MessageComposer.UserNftChatStylesMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(UserPurchasableChatStylesMessageComposer),
            new UserPurchasableChatStylesMessageComposerSerializer(
                MessageComposer.UserPurchasableChatStylesMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(UserPurchasableChatStyleChangedMessageComposer),
            new UserPurchasableChatStyleChangedMessageComposerSerializer(
                MessageComposer.UserPurchasableChatStyleChangedMessageComposer
            )
        );
    }
}
