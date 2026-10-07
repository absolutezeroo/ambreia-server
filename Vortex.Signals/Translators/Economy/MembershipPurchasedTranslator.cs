using System.Collections.Immutable;
using Vortex.Primitives.Events;
using Vortex.Primitives.Signals;

namespace Vortex.Signals.Translators;

/// <summary>Bought or renewed club. The amount is the months, for a Highest-mode task on membership.</summary>
public sealed class MembershipPurchasedTranslator : ISignalTranslator<MembershipPurchasedEvent>
{
    public static ImmutableArray<SignalShape> Shapes { get; } =
    [new(SignalActions.BuyMembership, [Facts.Months, Facts.Price])];

    public ImmutableArray<ProgressSignal> Translate(MembershipPurchasedEvent e) =>
        [
            new(
                e.PlayerId,
                SignalActions.BuyMembership,
                e.Months,
                Target: null,
                SignalFacts
                    .Build()
                    .Number(Facts.Months, e.TotalMonths)
                    .Number(Facts.Price, e.CreditCost)
            ),
        ];
}
