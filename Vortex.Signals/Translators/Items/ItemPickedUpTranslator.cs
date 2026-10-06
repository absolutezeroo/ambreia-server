using System.Collections.Immutable;
using Vortex.Primitives.Events;
using Vortex.Primitives.Signals;

namespace Vortex.Signals.Translators;

/// <summary>
/// Furniture taken back out of a room.
/// </summary>
/// <remarks>
/// No client artwork exists for this, so it makes a poor first step and a good later one — "place
/// it, walk on it, then pick it up" is the shape it was added for.
/// </remarks>
public sealed class ItemPickedUpTranslator : ISignalTranslator<ItemPickedUpEvent>
{
    public static ImmutableArray<SignalShape> Shapes { get; } =
    [new(SignalActions.PickUpItem, [Facts.Item, Facts.Room], TargetKind: FactKind.OpaqueId)];

    public ImmutableArray<ProgressSignal> Translate(ItemPickedUpEvent e) =>
        [
            new(
                e.ActorPlayerId,
                SignalActions.PickUpItem,
                1,
                Target: SignalValue.Id(e.ItemId),
                SignalFacts.Build().Id(Facts.Item, e.ItemId).Id(Facts.Room, e.RoomId)
            ),
        ];
}
