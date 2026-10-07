using Vortex.Primitives.Networking.Revisions;
using Vortex.Protocol.Messages.Outgoing.Collectibles;
using Vortex.Revisions.Revision20260701.Serializers.Collectibles;

namespace Vortex.Revisions.Revision20260701.Maps;

/// <summary>
/// What is left of the collectibles protocol: the Emerald and Silver wallet balances, which the
/// client files under collectibles but which belong to the wallet. The NFT, minting and store
/// messages were removed with their feature, the chat style lists with chat styles.
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
    }
}
