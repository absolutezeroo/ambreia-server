using System.Collections.Immutable;
using Vortex.Primitives.Events;
using Vortex.Primitives.Signals;

namespace Vortex.Signals.Translators;

/// <summary>
/// Furniture moved within a room, and furniture turned on the spot.
/// </summary>
/// <remarks>
/// One translator where there were two handlers on the same event, and it raises <b>both</b> actions
/// for a rotation. That is deliberate and predates this design: the client has no separate rotate
/// message, so every rotation has always raised <c>ItemMovedEvent</c>, and quietly excluding
/// rotations from <c>move_item</c> would make existing tasks count less than the day they were
/// written. <c>RotatedInPlace</c> is what makes the narrower signal possible, not a
/// reclassification of the wider one.
/// </remarks>
public sealed class ItemMovedTranslator : ISignalTranslator<ItemMovedEvent>
{
    public static ImmutableArray<SignalShape> Shapes { get; } =
    [
        new(SignalActions.MoveItem, [Facts.Item, Facts.Room]),
        new(SignalActions.RotateItem, [Facts.Item, Facts.Room]),
    ];

    public ImmutableArray<ProgressSignal> Translate(ItemMovedEvent e)
    {
        ImmutableArray<SignalFact> facts = SignalFacts
            .Build()
            .Id(Facts.Item, e.ItemId)
            .Id(Facts.Room, e.RoomId);

        return e.RotatedInPlace
            ?
            [
                new(e.ActorPlayerId, SignalActions.MoveItem, 1, Target: null, facts),
                new(e.ActorPlayerId, SignalActions.RotateItem, 1, Target: null, facts),
            ]
            : [new(e.ActorPlayerId, SignalActions.MoveItem, 1, Target: null, facts)];
    }
}
