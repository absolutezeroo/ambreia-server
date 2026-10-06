using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Rooms.Games;
using Vortex.Primitives.Rooms.Games.Components;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Rooms.Games;
using Vortex.Rooms.Games.Abstractions;
using Vortex.Rooms.Games.Arena;
using Vortex.Rooms.Games.Teams;
using Vortex.Rooms.Object.Logic.Furniture.Floor.Games;

namespace Vortex.Rooms.Tests.Games;

/// <summary>
/// A game with no rules, visible only in this assembly (<c>TestRoomGames</c> loads it beside the real
/// ones), so the runtime tests still have a real <c>[RoomGame]</c> to host now that no shipped game
/// is left. It needs one <see cref="TestOnlyArenaTile"/> to be startable and keeps one score per
/// team; two tiles further apart than <see cref="Separation"/> are two independent arenas.
/// </summary>
[RoomGame]
public sealed class TestOnlyGame(IRoomGameContext context) : RoomGameModule(context)
{
    public static readonly GameId Id = new("test_only");

    public const int Separation = 4;

    public override GameProfile Profile { get; } =
        new()
        {
            Id = Id,
            Teams = TeamSet.HabboColours,
            ArenaSeparation = Separation,
        };

    public override ArenaValidation ValidateArena() =>
        ArenaValidation
            .Builder()
            .Require("Test tiles", _context.Arena.CountOf<IArenaTileComponent>())
            .Build();
}

/// <summary>The one piece of furniture <see cref="TestOnlyGame"/> has: a bare arena tile.</summary>
public sealed class TestOnlyArenaTile(IStuffDataFactory stuffDataFactory, IRoomFloorItemContext ctx)
    : GameFurnitureLogic(stuffDataFactory, ctx),
        IArenaTileComponent
{
    public override GameId Game => TestOnlyGame.Id;
}
