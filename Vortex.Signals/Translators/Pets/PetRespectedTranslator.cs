using System.Collections.Immutable;
using Vortex.Primitives.Events;
using Vortex.Primitives.Signals;

namespace Vortex.Signals.Translators;

/// <summary>
/// Somebody scratched a pet. Both sides count, and the hotel counts them separately.
/// </summary>
/// <remarks>
/// One signal per side, as <c>FriendAcceptedTranslator</c> does: "scratching anyone's pets" is the
/// scratcher's, "having your pets scratched" is the owner's, and scratching your own pet is one act
/// that legitimately advances both. The owner's signal is dropped when they are the same player
/// only if the hotel said so — it does not, so it is not.
/// </remarks>
public sealed class PetRespectedTranslator : ISignalTranslator<PetRespectedEvent>
{
    public static ImmutableArray<SignalShape> Shapes { get; } =
    [
        new(SignalActions.RespectPet, [Facts.Pet], TargetKind: FactKind.OpaqueId),
        new(SignalActions.PetRespected, [Facts.Pet], TargetKind: FactKind.OpaqueId),
    ];

    public ImmutableArray<ProgressSignal> Translate(PetRespectedEvent e) =>
        [
            new(
                e.ActorId.Value,
                SignalActions.RespectPet,
                1,
                SignalValue.Id(e.PetId),
                SignalFacts.Build().Id(Facts.Pet, e.PetId)
            ),
            new(
                e.OwnerId.Value,
                SignalActions.PetRespected,
                1,
                SignalValue.Id(e.PetId),
                SignalFacts.Build().Id(Facts.Pet, e.PetId)
            ),
        ];
}
