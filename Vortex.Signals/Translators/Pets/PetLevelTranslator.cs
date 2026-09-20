using System.Collections.Immutable;
using Vortex.Primitives.Events;
using Vortex.Primitives.Signals;

namespace Vortex.Signals.Translators;

/// <summary>
/// A pet reached a new level.
/// </summary>
/// <remarks>
/// The amount is the level reached, not one: the client's own task is "get a pet to level N", which
/// is a Highest-mode task over the level. A counter-mode task on this action would add levels
/// together, which is content's mistake to make and not this translator's to prevent. The credit
/// goes to the pet's owner, who need not be whoever fed it.
/// </remarks>
public sealed class PetLevelTranslator : ISignalTranslator<PetLeveledUpEvent>
{
    public static ImmutableArray<SignalShape> Shapes { get; } =
    [new(SignalActions.PetLevel, [Facts.Pet, Facts.PetType], TargetKind: FactKind.OpaqueId)];

    public ImmutableArray<ProgressSignal> Translate(PetLeveledUpEvent e) =>
        [
            new(
                e.OwnerId.Value,
                SignalActions.PetLevel,
                e.Level,
                Target: SignalValue.Id(e.PetId),
                SignalFacts.Build().Id(Facts.Pet, e.PetId).Number(Facts.PetType, e.Type)
            ),
        ];
}
