using System.Collections.Immutable;
using Vortex.Primitives.Events;
using Vortex.Primitives.Signals;

namespace Vortex.Signals.Translators;

/// <summary>
/// A player fed a pet by hand.
/// </summary>
/// <remarks>
/// The amount is the food's nutrition, not one: the hotel's achievement is "for giving pets
/// %limit% points of food", so a bowl worth twenty counts twenty. Credited to whoever did the
/// feeding, which is often not the pet's owner.
/// </remarks>
public sealed class PetFedTranslator : ISignalTranslator<PetFedEvent>
{
    public static ImmutableArray<SignalShape> Shapes { get; } =
    [new(SignalActions.FeedPet, [Facts.Pet, Facts.PetType], TargetKind: FactKind.OpaqueId)];

    public ImmutableArray<ProgressSignal> Translate(PetFedEvent e) =>
        [
            new(
                e.ActorId.Value,
                SignalActions.FeedPet,
                e.Nutrition,
                SignalValue.Id(e.PetId),
                SignalFacts.Build().Id(Facts.Pet, e.PetId).Number(Facts.PetType, e.Type)
            ),
        ];
}
