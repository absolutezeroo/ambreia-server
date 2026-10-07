using System.Collections.Immutable;
using Vortex.Primitives.Events;
using Vortex.Primitives.Signals;

namespace Vortex.Signals.Translators;

/// <summary>Claimed a club gift.</summary>
public sealed class MembershipGiftTranslator : ISignalTranslator<MembershipGiftClaimedEvent>
{
    public static ImmutableArray<SignalShape> Shapes { get; } =
    [new(SignalActions.ClaimMembershipGift, [Facts.MembershipGift], TargetKind: FactKind.Text)];

    public ImmutableArray<ProgressSignal> Translate(MembershipGiftClaimedEvent e) =>
        [
            new(
                e.PlayerId,
                SignalActions.ClaimMembershipGift,
                1,
                e.ProductCode,
                SignalFacts.Build().Text(Facts.MembershipGift, e.ProductCode)
            ),
        ];
}
