using System.Collections.Immutable;
using Vortex.Primitives.Events;
using Vortex.Primitives.Signals;

namespace Vortex.Signals.Translators;

/// <summary>
/// Bought something for somebody else. One signal per side, as FriendAcceptedTranslator does:
/// the hotel counts giving gifts and receiving them as two different achievements, and only this
/// event knows both players. Unwrapping the present is a different act and credits the unwrapper,
/// who may well have bought it themselves -- which is why receiving is counted here instead.
/// </summary>
public sealed class CatalogGiftTranslator : ISignalTranslator<CatalogGiftPurchasedEvent>
{
    public static ImmutableArray<SignalShape> Shapes { get; } =
    [
        new(
            SignalActions.BuyGift,
            [Facts.Offer, Facts.Player, Facts.Price],
            TargetKind: FactKind.OfferId
        ),
        new(SignalActions.ReceiveGift, [Facts.Offer, Facts.Player], TargetKind: FactKind.OfferId),
    ];

    public ImmutableArray<ProgressSignal> Translate(CatalogGiftPurchasedEvent e) =>
        [
            new(
                e.BuyerPlayerId,
                SignalActions.BuyGift,
                1,
                SignalValue.Id(e.OfferId),
                SignalFacts
                    .Build()
                    .Id(Facts.Offer, e.OfferId)
                    .Id(Facts.Player, e.ReceiverPlayerId)
                    .Number(Facts.Price, e.CreditCost)
            ),
            new(
                e.ReceiverPlayerId,
                SignalActions.ReceiveGift,
                1,
                SignalValue.Id(e.OfferId),
                SignalFacts.Build().Id(Facts.Offer, e.OfferId).Id(Facts.Player, e.BuyerPlayerId)
            ),
        ];
}
