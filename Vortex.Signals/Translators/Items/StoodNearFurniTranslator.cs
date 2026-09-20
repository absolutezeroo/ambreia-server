using System.Collections.Immutable;
using Vortex.Primitives.Events;
using Vortex.Primitives.Players.Avatar;
using Vortex.Primitives.Signals;

namespace Vortex.Signals.Translators;

/// <summary>
/// Standing beside a piece of furniture, or on it.
/// </summary>
/// <remarks>
/// The signal that answers a whole condition at once, which is why it carries the look as well as
/// the place. "Barefoot, in front of the television" written as two steps is two moments with a gap
/// between them; written as one step it is one instant, and there is nothing to slip through.
/// <para>
/// The target is the piece of furniture, so a distinct-mode task counts different pieces and a
/// later step can point back at the one this matched.
/// </para>
/// </remarks>
public sealed class StoodNearFurniTranslator : ISignalTranslator<PlayerStoodNearFurniEvent>
{
    public static ImmutableArray<SignalShape> Shapes { get; } =
    [
        new(
            SignalActions.StandNearFurni,
            [
                Facts.Item,
                Facts.Definition,
                Facts.Relation,
                Facts.Room,
                Facts.Figure,
                Facts.FigureSet,
                Facts.Posture,
            ],
            TargetKind: FactKind.OpaqueId
        ),
    ];

    public ImmutableArray<ProgressSignal> Translate(PlayerStoodNearFurniEvent e) =>
        [
            new(
                e.PlayerId,
                SignalActions.StandNearFurni,
                1,
                Target: SignalValue.Id(e.ItemId),
                SignalFacts
                    .Build()
                    .Id(Facts.Item, e.ItemId)
                    .Id(Facts.Definition, e.DefinitionId)
                    .Enum(Facts.Relation, e.Relation)
                    .Id(Facts.Room, e.RoomId)
                    .Text(Facts.Figure, e.Figure)
                    .Ids(Facts.FigureSet, FigureString.SetIdsOf(e.Figure))
                    // Text, not Enum: Enum adds unconditionally, and a room that did not say how
                    // the avatar was holding itself has not said it was standing.
                    .Text(Facts.Posture, e.Posture)
            ),
        ];
}
