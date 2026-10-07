using System.Collections.Immutable;
using Vortex.Primitives.Events;
using Vortex.Primitives.Signals;

namespace Vortex.Signals.Translators;

/// <summary>Claimed vault income, from one category of holdings.</summary>
public sealed class RewardVaultTranslator : ISignalTranslator<RewardVaultClaimedEvent>
{
    public static ImmutableArray<SignalShape> Shapes { get; } =
    [
        new(
            SignalActions.ClaimRewardVault,
            [Facts.RewardVaultCategory, Facts.Quantity],
            TargetKind: FactKind.Text
        ),
    ];

    public ImmutableArray<ProgressSignal> Translate(RewardVaultClaimedEvent e) =>
        [
            new(
                e.PlayerId.Value,
                SignalActions.ClaimRewardVault,
                e.Rewards,
                e.Category,
                SignalFacts
                    .Build()
                    .Text(Facts.RewardVaultCategory, e.Category)
                    .Number(Facts.Quantity, e.Rewards)
            ),
        ];
}
