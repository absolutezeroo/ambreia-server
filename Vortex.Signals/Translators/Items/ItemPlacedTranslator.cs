using System.Collections.Immutable;
using Vortex.Primitives.Events;
using Vortex.Primitives.Signals;

namespace Vortex.Signals.Translators;

/// <summary>Furniture placed. The target is the definition id, so a task can require a kind of furni.</summary>
public sealed class ItemPlacedTranslator : ISignalTranslator<ItemPlacedEvent>
{
    public static ImmutableArray<SignalShape> Shapes { get; } =
    [
        new(
            SignalActions.PlaceItem,
            [Facts.Item, Facts.Definition, Facts.Placement, Facts.Room],
            TargetKind: FactKind.FurnitureId
        ),
    ];

    public ImmutableArray<ProgressSignal> Translate(ItemPlacedEvent e) =>
        [
            new(
                e.ActorPlayerId,
                SignalActions.PlaceItem,
                1,
                Target: SignalValue.Id(e.DefinitionId),
                SignalFacts
                    .Build()
                    .Id(Facts.Item, e.ItemId)
                    .Id(Facts.Definition, e.DefinitionId)
                    .Enum(
                        Facts.Placement,
                        e.IsWallItem ? Facts.PlacementWall : Facts.PlacementFloor
                    )
                    .Id(Facts.Room, e.RoomId)
            ),
        ];
}
