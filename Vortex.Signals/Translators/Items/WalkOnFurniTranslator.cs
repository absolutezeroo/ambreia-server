using System.Collections.Immutable;
using Vortex.Primitives.Events;
using Vortex.Primitives.Signals;

namespace Vortex.Signals.Translators;

/// <summary>
/// Stepping onto a piece of floor furniture.
/// </summary>
/// <remarks>
/// The highest-frequency signal in the hotel by a wide margin — once per tile walked onto, for every
/// avatar in every room. It is only affordable because the interest gate runs on the calling thread
/// before this translator is reached: with no content naming <c>walk_on_furni</c>, a walked tile is
/// one hash lookup and a return, which is what the room tick's own comment counts on.
/// </remarks>
public sealed class WalkOnFurniTranslator : ISignalTranslator<PlayerWalkedOnFurniEvent>
{
    public static ImmutableArray<SignalShape> Shapes { get; } =
    [
        new(
            SignalActions.WalkOnFurni,
            [Facts.Item, Facts.Definition, Facts.Room],
            TargetKind: FactKind.OpaqueId
        ),
    ];

    public ImmutableArray<ProgressSignal> Translate(PlayerWalkedOnFurniEvent e) =>
        [
            new(
                e.PlayerId,
                SignalActions.WalkOnFurni,
                1,
                Target: SignalValue.Id(e.ItemId),
                SignalFacts
                    .Build()
                    .Id(Facts.Item, e.ItemId)
                    .Id(Facts.Definition, e.DefinitionId)
                    .Id(Facts.Room, e.RoomId)
            ),
        ];
}
