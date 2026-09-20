using System.Collections.Immutable;
using Vortex.Primitives.Events;
using Vortex.Primitives.Signals;

namespace Vortex.Signals.Translators;

/// <summary>
/// A pet obeyed an order.
/// </summary>
/// <remarks>
/// Credited to whoever gave it, which the room has already checked is the pet's own owner — a
/// command from anyone else is refused before this is ever raised, so the actor and the owner are
/// the same player and only one signal is needed.
/// <para>
/// The target is the pet, like every other pet action, so "train the same pet ten times" is a
/// distinct-mode task and a later step can point back at it. The order itself rides as a fact,
/// because "make a pet sit" and "make a pet play dead" are two different tasks about one action.
/// </para>
/// </remarks>
public sealed class PetCommandedTranslator : ISignalTranslator<PetCommandObeyedEvent>
{
    public static ImmutableArray<SignalShape> Shapes { get; } =
    [
        new(
            SignalActions.CommandPet,
            [Facts.Pet, Facts.PetType, Facts.PetCommand],
            TargetKind: FactKind.OpaqueId
        ),
    ];

    public ImmutableArray<ProgressSignal> Translate(PetCommandObeyedEvent e) =>
        [
            new(
                e.ActorId.Value,
                SignalActions.CommandPet,
                1,
                SignalValue.Id(e.PetId),
                SignalFacts
                    .Build()
                    .Id(Facts.Pet, e.PetId)
                    .Number(Facts.PetType, e.Type)
                    // Id, not IdIfAny: command 0 is "Free", a real order, and dropping it would
                    // make the one order every pet knows from level zero the one nothing counts.
                    .Id(Facts.PetCommand, e.CommandId)
            ),
        ];
}
