using Vortex.Primitives.Networking.Revisions;
using Vortex.Protocol.Messages.Outgoing.Collectibles;
using Vortex.Protocol.Messages.Outgoing.Nft;
using Vortex.Revisions.Configuration;
using Vortex.Revisions.Revision20260701.Parsers.Collectibles;
using Vortex.Revisions.Revision20260701.Serializers.Collectibles;
using Vortex.Revisions.Revision20260701.Serializers.Nft;

namespace Vortex.Revisions.Revision20260701.Maps;

internal sealed class CollectiblesMap : IRevisionMap
{
    private readonly ProtocolLimitsConfig _protocolLimits;

    public CollectiblesMap(ProtocolLimitsConfig protocolLimits)
    {
        _protocolLimits = protocolLimits;
    }

    public void RegisterInto(IRevisionMapBuilder builder)
    {
        // Relics are offered through the ordinary trade window, so they are bounded by the same
        // per-side limit as furniture rather than a rule of their own.
        builder.MapParser(
            MessageEvent.AddNftToTradeEvent,
            new AddNftToTradeMessageParser(_protocolLimits.MaxTradeItems)
        );
        builder.MapParser(
            MessageEvent.RemoveNftFromTradeEvent,
            new RemoveNftFromTradeMessageParser()
        );
        builder.MapSerializer(
            typeof(TradeNftAssetsMessageComposer),
            new TradeNftAssetsMessageComposerSerializer(
                MessageComposer.TradeNftAssetsMessageComposer
            )
        );
        builder.MapParser(
            MessageEvent.GetCollectibleMintableItemTypesMessageEvent,
            new GetCollectibleMintableItemTypesMessageParser()
        );
        builder.MapParser(
            MessageEvent.GetCollectibleMintingEnabledMessageEvent,
            new GetCollectibleMintingEnabledMessageParser()
        );
        builder.MapParser(
            MessageEvent.GetCollectibleMintTokensMessageEvent,
            new GetCollectibleMintTokensMessageParser()
        );
        builder.MapParser(
            MessageEvent.GetNftStoreOffersMessageEvent,
            new GetNftStoreOffersMessageParser()
        );
        builder.MapParser(MessageEvent.GetNftClaimsMessageEvent, new GetNftClaimsMessageParser());
        builder.MapParser(
            MessageEvent.ClaimNftClaimsMessageEvent,
            new ClaimNftClaimsMessageParser()
        );
        builder.MapParser(
            MessageEvent.TransferNftAssetsMessageEvent,
            new TransferNftAssetsMessageParser()
        );
        builder.MapParser(
            MessageEvent.NftStorePurchaseMessageEvent,
            new NftStorePurchaseMessageParser()
        );

        builder.MapSerializer(
            typeof(NftStorePurchaseMessageComposer),
            new NftStorePurchaseMessageComposerSerializer(
                MessageComposer.NftStorePurchaseMessageComposer
            )
        );

        builder.MapSerializer(
            typeof(NftStoreOffersMessageComposer),
            new NftStoreOffersMessageComposerSerializer(
                MessageComposer.NftStoreOffersMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(NftClaimsMessageComposer),
            new NftClaimsMessageComposerSerializer(MessageComposer.NftClaimsMessageComposer)
        );
        builder.MapSerializer(
            typeof(NftClaimResultMessageComposer),
            new NftClaimResultMessageComposerSerializer(
                MessageComposer.NftClaimResultMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(RedeemNftLootBoxStateMessageComposer),
            new RedeemNftLootBoxStateMessageComposerSerializer(
                MessageComposer.RedeemNftLootBoxStateMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(RedeemNftLootBoxResultMessageComposer),
            new RedeemNftLootBoxResultMessageComposerSerializer(
                MessageComposer.RedeemNftLootBoxResultMessageComposer
            )
        );
        builder.MapParser(
            MessageEvent.GetCollectibleWalletAddressesMessageEvent,
            new GetCollectibleWalletAddressesMessageParser()
        );
        builder.MapParser(
            MessageEvent.GetCollectorScoreMessageEvent,
            new GetCollectorScoreMessageParser()
        );
        builder.MapParser(
            MessageEvent.GetMintTokenOffersMessageEvent,
            new GetMintTokenOffersMessageParser()
        );
        builder.MapParser(
            MessageEvent.GetNftCollectionsMessageEvent,
            new GetNftCollectionsMessageParser()
        );
        builder.MapParser(
            MessageEvent.GetNftTransferFeeMessageEvent,
            new GetNftTransferFeeMessageParser()
        );
        builder.MapParser(MessageEvent.MintItemMessageEvent, new MintItemMessageParser());
        builder.MapParser(
            MessageEvent.NftCollectiblesClaimBonusItemMessageEvent,
            new NftCollectiblesClaimBonusItemMessageParser()
        );
        builder.MapParser(
            MessageEvent.NftCollectiblesClaimRewardItemMessageEvent,
            new NftCollectiblesClaimRewardItemMessageParser()
        );
        builder.MapParser(
            MessageEvent.GetNftAssetInventoryMessageEvent,
            new GetNftAssetInventoryMessageParser()
        );
        builder.MapParser(
            MessageEvent.PurchaseMintTokenMessageEvent,
            new PurchaseMintTokenMessageParser()
        );

        builder.MapSerializer(
            typeof(CollectableMintableItemTypesMessageComposer),
            new CollectableMintableItemTypesMessageComposerSerializer(
                MessageComposer.CollectableMintableItemTypesMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(CollectibleMintableItemResultMessageComposer),
            new CollectibleMintableItemResultMessageComposerSerializer(
                MessageComposer.CollectibleMintableItemResultMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(CollectibleMintingEnabledMessageComposer),
            new CollectibleMintingEnabledMessageComposerSerializer(
                MessageComposer.CollectibleMintingEnabledMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(CollectibleMintTokenCountMessageComposer),
            new CollectibleMintTokenCountMessageComposerSerializer(
                MessageComposer.CollectibleMintTokenCountMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(CollectibleMintTokenOffersMessageComposer),
            new CollectibleMintTokenOffersMessageComposerSerializer(
                MessageComposer.CollectibleMintTokenOffersMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(CollectibleWalletAddressesMessageComposer),
            new CollectibleWalletAddressesMessageComposerSerializer(
                MessageComposer.CollectibleWalletAddressesMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(EmeraldBalanceMessageComposer),
            new EmeraldBalanceMessageComposerSerializer(
                MessageComposer.EmeraldBalanceMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(NftBonusItemClaimResultMessageComposer),
            new NftBonusItemClaimResultMessageComposerSerializer(
                MessageComposer.NftBonusItemClaimResultMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(NftCollectionsMessageComposer),
            new NftCollectionsMessageComposerSerializer(
                MessageComposer.NftCollectionsMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(NftCollectionsScoreMessageComposer),
            new NftCollectionsScoreMessageComposerSerializer(
                MessageComposer.NftCollectionsScoreMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(NftRewardItemClaimResultMessageComposer),
            new NftRewardItemClaimResultMessageComposerSerializer(
                MessageComposer.NftRewardItemClaimResultMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(TradeNftAssetInventoryMessageComposer),
            new TradeNftAssetInventoryMessageComposerSerializer(
                MessageComposer.TradeNftAssetInventoryMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(NftTransferAssetsResultMessageComposer),
            new NftTransferAssetsResultMessageComposerSerializer(
                MessageComposer.NftTransferAssetsResultMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(NftTransferFeeMessageComposer),
            new NftTransferFeeMessageComposerSerializer(
                MessageComposer.NftTransferFeeMessageComposer
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
