using Vortex.Primitives.Networking.Revisions;
using Vortex.Protocol.Messages.Outgoing.Vault;
using Vortex.Revisions.Revision20260701.Parsers.Vault;
using Vortex.Revisions.Revision20260701.Serializers.Vault;

namespace Vortex.Revisions.Revision20260701.Maps;

internal sealed class VaultMap : IRevisionMap
{
    public void RegisterInto(IRevisionMapBuilder builder)
    {
        builder.MapParser(
            MessageEvent.IncomeRewardClaimMessageEvent,
            new IncomeRewardClaimMessageParser()
        );
        builder.MapParser(
            MessageEvent.IncomeRewardStatusMessageEvent,
            new IncomeRewardStatusMessageParser()
        );

        builder.MapSerializer(
            typeof(IncomeRewardClaimResponseMessageComposer),
            new IncomeRewardClaimResponseMessageComposerSerializer(
                MessageComposer.IncomeRewardClaimResponseMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(IncomeRewardNotificationMessageComposer),
            new IncomeRewardNotificationMessageComposerSerializer(
                MessageComposer.IncomeRewardNotificationMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(IncomeRewardStatusMessageComposer),
            new IncomeRewardStatusMessageComposerSerializer(
                MessageComposer.IncomeRewardStatusMessageComposer
            )
        );
    }
}
