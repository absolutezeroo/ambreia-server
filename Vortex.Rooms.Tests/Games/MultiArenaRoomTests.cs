using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Enums.Games;
using Vortex.Primitives.Rooms.Events;
using Vortex.Primitives.Rooms.Games;
using Vortex.Primitives.Rooms.Object;
using Vortex.Rooms.Games.Runtime;
using Vortex.Rooms.Object.Logic.Furniture.Floor;
using Vortex.Rooms.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Games;

/// <summary>
/// A room holding more than one playable game, driven through the real runtime and the real
/// furniture. This is the scenario the old architecture got wrong: a hall with two separate arenas
/// answered a single press of a single counter by starting BOTH matches.
/// </summary>
public sealed class MultiArenaRoomTests
{
    private static TestOnlyArenaTile PlaceTile(RoomHarness harness, int x, int y) =>
        GameFurni.Place(
            harness,
            "test_tile",
            x,
            y,
            (factory, ctx) => new TestOnlyArenaTile(factory, ctx)
        );

    /// <summary>Builds a room with one arena in the north-west corner and one in the south-east.</summary>
    private static void BuildHall(RoomHarness harness)
    {
        PlaceTile(harness, 1, 1);
        PlaceTile(harness, 2, 1);

        PlaceTile(harness, 9, 9);
        PlaceTile(harness, 10, 9);
    }

    private static Task<bool> StartAsync(RoomHarness harness, RoomObjectId source = default) =>
        harness.Grain.GameRuntime.StartGameAsync(source, GameId.None, CancellationToken.None);

    private static int RunningArenas(RoomHarness harness) =>
        harness.Grain.GameRuntime.Arenas.Count(arena =>
            harness.Grain.GameRuntime.PhaseOf(arena) == GamePhase.Running
        );

    [Fact]
    public async Task AHallWithTwoArenas_StartsNeitherOnABareRequest()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);
        BuildHall(harness);

        bool started = await StartAsync(harness).ConfigureAwait(true);

        // The regression, stated plainly: one request, two valid arenas, nothing to choose between
        // them. It used to start both.
        started.Should().BeFalse();
        harness.Grain.GameRuntime.Arenas.Should().HaveCount(2);
        RunningArenas(harness).Should().Be(0);

        // The room's round still opens: it is a room-level concept, not a match, and the point is
        // that it opened with NO match under it.
        harness.RoomEvents.OfType<WiredGameStartedEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task ACounterBesideOneArena_StartsThatArenaAndOnlyThat()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);
        BuildHall(harness);
        FurnitureGameTimerLogic counter = PlaceCounter(harness, 2, 2);

        bool started = await StartAsync(harness, counter.Context.ObjectId).ConfigureAwait(true);

        started.Should().BeTrue();
        RunningArenas(harness).Should().Be(1, "the other arena is at the other end of the hall");
    }

    [Fact]
    public async Task StoppingFromOneCounter_LeavesTheOtherMatchRunning()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);
        BuildHall(harness);
        FurnitureGameTimerLogic firstCounter = PlaceCounter(harness, 2, 2);
        FurnitureGameTimerLogic secondCounter = PlaceCounter(harness, 9, 8);
        await StartAsync(harness, firstCounter.Context.ObjectId).ConfigureAwait(true);
        await StartAsync(harness, secondCounter.Context.ObjectId).ConfigureAwait(true);
        RunningArenas(harness).Should().Be(2);

        await EndAsync(harness, secondCounter).ConfigureAwait(true);

        RunningArenas(harness).Should().Be(1, "stopping one match must not reach across the room");
    }

    [Fact]
    public async Task TheRoomsRound_SpansEveryArena()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);
        BuildHall(harness);
        FurnitureGameTimerLogic firstCounter = PlaceCounter(harness, 2, 2);
        FurnitureGameTimerLogic secondCounter = PlaceCounter(harness, 9, 8);

        await StartAsync(harness, firstCounter.Context.ObjectId).ConfigureAwait(true);
        await StartAsync(harness, secondCounter.Context.ObjectId).ConfigureAwait(true);

        // GAME_STARTS / GAME_ENDS are room-level triggers. Two matches are still one round.
        harness.RoomEvents.OfType<WiredGameStartedEvent>().Should().ContainSingle();

        await EndAsync(harness, firstCounter).ConfigureAwait(true);
        harness
            .RoomEvents.OfType<WiredGameEndedEvent>()
            .Should()
            .BeEmpty("one arena is still playing");

        await EndAsync(harness, secondCounter).ConfigureAwait(true);
        harness.RoomEvents.OfType<WiredGameEndedEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task TwoArenas_HaveMatchIdsTheOtherCannotShare()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);
        BuildHall(harness);
        FurnitureGameTimerLogic firstCounter = PlaceCounter(harness, 2, 2);
        FurnitureGameTimerLogic secondCounter = PlaceCounter(harness, 9, 8);

        await StartAsync(harness, firstCounter.Context.ObjectId).ConfigureAwait(true);
        await StartAsync(harness, secondCounter.Context.ObjectId).ConfigureAwait(true);

        // Deferred work carries its match id and drops itself when it no longer matches. Two live
        // matches in one room must therefore be distinguishable, arena included.
        ArenaId first = new(TestOnlyGame.Id, 0);
        ArenaId second = new(TestOnlyGame.Id, 1);

        harness.Grain.GameRuntime.Arenas.Should().Contain([first, second]);
        new MatchId(harness.Grain.RoomId, first, 1)
            .Should()
            .NotBe(new MatchId(harness.Grain.RoomId, second, 1));
    }

    [Fact]
    public async Task ARoomWithOneValidArena_StillStartsFromABareRequest()
    {
        // The ordinary room, and the reason the resolver's last rule exists: only one arena can
        // start, so there is nothing to confuse.
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);
        PlaceTile(harness, 1, 1);
        PlaceTile(harness, 2, 1);

        bool started = await StartAsync(harness).ConfigureAwait(true);

        started.Should().BeTrue();
        RunningArenas(harness).Should().Be(1);
    }

    private static Task EndAsync(RoomHarness harness, FurnitureGameTimerLogic counter) =>
        harness.Grain.GameRuntime.EndGameAsync(
            counter.Context.ObjectId,
            GameId.None,
            CancellationToken.None
        );

    private static FurnitureGameTimerLogic PlaceCounter(RoomHarness harness, int x, int y) =>
        GameFurni.Place(
            harness,
            "game_timer",
            x,
            y,
            (factory, ctx) => new FurnitureGameTimerLogic(factory, ctx)
        );
}
