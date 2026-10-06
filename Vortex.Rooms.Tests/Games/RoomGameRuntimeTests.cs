using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums.Games;
using Vortex.Primitives.Rooms.Events;
using Vortex.Primitives.Rooms.Games;
using Vortex.Rooms.Games.Abstractions;
using Vortex.Rooms.Games.Arena;
using Vortex.Rooms.Games.Scoring;
using Vortex.Rooms.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Games;

/// <summary>
/// The seam every room game plugs into. Nothing which starts or stops a round — the game-timer furni,
/// the wired control-clock action, the tick loop, the avatar-left path — names an individual game:
/// they all go through the runtime, which resolves ONE arena to act on.
/// <para>
/// The resolution is the point. "Start the game" used to mean "start every game whose arena
/// validates", so one press of one counter in a hall with three arenas kicked off three unrelated
/// matches. These tests pin the replacement: a start has a target or it does nothing.
/// </para>
/// </summary>
public sealed class RoomGameRuntimeTests
{
    [Fact]
    public async Task EveryShippedGame_IsHostedByEveryRoom()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        // A game that exists but was never plugged in is invisible: it builds, it tests and it never
        // runs. The harness loads the games exactly the way production does — the real feature
        // processor over the real assembly — so this also covers the attribute and the DI wiring.
        IEnumerable<GameId> hosted = harness.Grain.GameRuntime.Games.Select(game =>
            game.Profile.Id
        );

        hosted
            .Should()
            .Contain(
                [TestOnlyGame.Id],
                "adding a game is a [RoomGame] attribute, and every room hosts it from then on"
            );
    }

    /// <summary>An untargeted start, the way a bare counter or a wired clock makes one.</summary>
    private static Task<bool> StartAsync(RoomHarness harness, string game = "") =>
        harness.Grain.GameRuntime.StartGameAsync(default, new GameId(game), CancellationToken.None);

    private static Task<bool> EndAsync(RoomHarness harness, string game = "") =>
        harness.Grain.GameRuntime.EndGameAsync(default, new GameId(game), CancellationToken.None);

    [Fact]
    public async Task OneValidGameInTheRoom_StartsOnAnUntargetedRequest()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);
        RecordingGame only = new("only");
        harness.Grain.GameRuntime.Register(_ => only);

        // The shipped games are hosted too, but none of their arenas validates in an empty room, so
        // there is exactly one candidate and nothing to be confused about. This is what keeps every
        // ordinary single-game room behaving as it always did.
        bool started = await StartAsync(harness).ConfigureAwait(true);

        started.Should().BeTrue();
        only.Starts.Should().Be(1);
    }

    [Fact]
    public async Task SeveralValidGamesAndNothingToChooseBetweenThem_StartsNONEOfThem()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);
        RecordingGame first = new("first");
        RecordingGame second = new("second");
        harness.Grain.GameRuntime.Register(_ => first);
        harness.Grain.GameRuntime.Register(_ => second);

        bool started = await StartAsync(harness).ConfigureAwait(true);

        // THE regression this whole change exists for: a hall with three different arenas answered one
        // press of one counter with three unrelated matches. An
        // ambiguous room now starts NO match and logs which arenas were in contention.
        started.Should().BeFalse();
        first.Starts.Should().Be(0);
        second.Starts.Should().Be(0);

        // The room's ROUND still opens, and that is not a loophole: GAME_STARTS is a room-level
        // wired trigger that fires in rooms with no game furniture at all. What an ambiguous room
        // refuses is picking a match, not running its wired.
        harness.RoomEvents.OfType<WiredGameStartedEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task NamingTheGame_StartsThatOneAndOnlyThatOne()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);
        RecordingGame first = new("first");
        RecordingGame second = new("second");
        harness.Grain.GameRuntime.Register(_ => first);
        harness.Grain.GameRuntime.Register(_ => second);

        bool started = await StartAsync(harness, "first").ConfigureAwait(true);

        started.Should().BeTrue();
        first.Starts.Should().Be(1);
        second.Starts.Should().Be(0, "an ambiguous room is disambiguated, not fanned out to");
    }

    [Fact]
    public async Task StoppingResolvesAgainstWhatIsRunning_AndLeavesTheOtherMatchAlone()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);
        RecordingGame first = new("first");
        RecordingGame second = new("second");
        harness.Grain.GameRuntime.Register(_ => first);
        harness.Grain.GameRuntime.Register(_ => second);
        await StartAsync(harness, "first").ConfigureAwait(true);
        await StartAsync(harness, "second").ConfigureAwait(true);

        await EndAsync(harness, "second").ConfigureAwait(true);

        second.RoundEnds.Should().Be(1);
        first.RoundEnds.Should().Be(0, "stopping one match must not reach across to the other");
        harness
            .RoomEvents.OfType<WiredGameEndedEvent>()
            .Should()
            .BeEmpty("the room's round is not over while a match is still running");
    }

    [Fact]
    public async Task TheRoomsRoundEnds_OnlyWhenTheLastMatchDoes()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);
        RecordingGame first = new("first");
        RecordingGame second = new("second");
        harness.Grain.GameRuntime.Register(_ => first);
        harness.Grain.GameRuntime.Register(_ => second);
        await StartAsync(harness, "first").ConfigureAwait(true);
        await StartAsync(harness, "second").ConfigureAwait(true);

        // GAME_STARTS / GAME_ENDS are ROOM-level wired triggers: a second arena kicking off is not a
        // second round, and the round is not over until the last arena has stopped.
        harness.RoomEvents.OfType<WiredGameStartedEvent>().Should().ContainSingle();

        await EndAsync(harness, "first").ConfigureAwait(true);
        await EndAsync(harness, "second").ConfigureAwait(true);

        harness.RoomEvents.OfType<WiredGameEndedEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task TheRoundIsAnnounced_BeforeAnyGamePrepares()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);
        RecordingGame game = new("first") { RoomEventCount = () => harness.RoomEvents.Count };
        harness.Grain.GameRuntime.Register(_ => game);

        await StartAsync(harness).ConfigureAwait(true);

        // A GAME_STARTS box wired to a give-score action has to have run before a game clears
        // anything: the ordering is what keeps its points from vanishing without a trace.
        harness
            .RoomEvents.OfType<WiredGameStartedEvent>()
            .Should()
            .ContainSingle("a room announces one round, however many games it hosts");
        game.RoomEventsSeenAtPrepare.Should().Be(1);
    }

    [Fact]
    public async Task StartingAnAlreadyRunningRound_StartsNothingTwice()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);
        RecordingGame game = new("first");
        harness.Grain.GameRuntime.Register(_ => game);

        await StartAsync(harness).ConfigureAwait(true);
        await StartAsync(harness).ConfigureAwait(true);

        game.Starts.Should().Be(1);
    }

    [Fact]
    public async Task EndingTheRound_EndsTheMatchAndAnnouncesIt()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);
        RecordingGame only = new("only");
        harness.Grain.GameRuntime.Register(_ => only);
        await StartAsync(harness).ConfigureAwait(true);

        await EndAsync(harness).ConfigureAwait(true);

        only.RoundEnds.Should().Be(1);
        harness.RoomEvents.OfType<WiredGameEndedEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task EveryMatchInTheRoom_CanBeEndedAtOnce()
    {
        // The room is being cleared rather than a game stopped — a shutdown, a moderator wipe — so
        // there is no target to resolve and every arena winds down.
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);
        RecordingGame first = new("first");
        RecordingGame second = new("second");
        harness.Grain.GameRuntime.Register(_ => first);
        harness.Grain.GameRuntime.Register(_ => second);
        await StartAsync(harness, "first").ConfigureAwait(true);
        await StartAsync(harness, "second").ConfigureAwait(true);

        await harness
            .Grain.GameRuntime.EndAllGamesAsync(CancellationToken.None)
            .ConfigureAwait(true);

        first.RoundEnds.Should().Be(1);
        second.RoundEnds.Should().Be(1);
        harness.RoomEvents.OfType<WiredGameEndedEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task ARoomWithNoGameFurnitureAtAll_StillRunsARound()
    {
        // The pure-wired room: a give-score box, some scoreboards, a timer, no arena. Its round has
        // to open and close, because that is what its GAME_STARTS and GAME_ENDS boxes run off.
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        bool started = await StartAsync(harness).ConfigureAwait(true);

        started.Should().BeFalse("there is no match to run");
        harness.RoomEvents.OfType<WiredGameStartedEvent>().Should().ContainSingle();

        await EndAsync(harness).ConfigureAwait(true);

        harness.RoomEvents.OfType<WiredGameEndedEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task EndingAnIdleRoom_DoesNothing()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);
        RecordingGame game = new("first");
        harness.Grain.GameRuntime.Register(_ => game);

        await EndAsync(harness).ConfigureAwait(true);

        game.RoundEnds.Should().Be(0);
        harness.RoomEvents.OfType<WiredGameEndedEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task AGameThatEndsTheRoundItself_DoesNotLoop()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);
        RecordingGame game = new("first");
        game.OnRoundEnd = ct => harness.Grain.GameRuntime.EndGameAsync(default, GameId.None, ct);
        harness.Grain.GameRuntime.Register(_ => game);
        await StartAsync(harness).ConfigureAwait(true);

        await EndAsync(harness).ConfigureAwait(true);

        game.RoundEnds.Should().Be(1);
        harness.RoomEvents.OfType<WiredGameEndedEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task AMatchRunsThroughItsPhasesInOrder_AndEndsIdleWithNoMatch()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);
        RecordingGame game = new("first");
        harness.Grain.GameRuntime.Register(_ => game);

        await StartAsync(harness).ConfigureAwait(true);
        await EndAsync(harness).ConfigureAwait(true);

        game.Phases.Should()
            .Equal(
                GamePhase.Preparing,
                GamePhase.Running,
                GamePhase.RoundEnding,
                GamePhase.Finished,
                GamePhase.Resetting,
                GamePhase.Idle
            );
        harness.Grain.GameRuntime.PhaseOf(new GameId("first")).Should().Be(GamePhase.Idle);
    }

    [Fact]
    public async Task SuccessiveMatchesInTheSameRoom_HaveDifferentIds()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);
        RecordingGame game = new("first");
        harness.Grain.GameRuntime.Register(_ => game);

        await StartAsync(harness).ConfigureAwait(true);
        await EndAsync(harness).ConfigureAwait(true);
        await StartAsync(harness).ConfigureAwait(true);

        // Without this, a callback from the previous round cannot tell it is stale, and the only
        // defence left is remembering to clear every queue at kick-off.
        game.MatchIds.Should().HaveCount(2);
        game.MatchIds.Distinct().Should().HaveCount(2);
    }

    [Fact]
    public async Task AGameWhoseArenaDoesNotValidate_DoesNotStart()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);
        RecordingGame game = new("first")
        {
            Validation = ArenaValidation.Builder().Require("Goals", found: 0, required: 2).Build(),
        };
        harness.Grain.GameRuntime.Register(_ => game);

        await StartAsync(harness).ConfigureAwait(true);

        game.Starts.Should().Be(0);
        harness.Grain.GameRuntime.PhaseOf(new GameId("first")).Should().Be(GamePhase.Idle);
    }

    [Fact]
    public async Task AGameThatThrowsOnPrepare_DoesNotStopTheOthers_AndDoesNotPlay()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);
        RecordingGame broken = new("broken")
        {
            OnPrepare = _ => throw new InvalidOperationException("no config"),
        };
        RecordingGame healthy = new("healthy");
        harness.Grain.GameRuntime.Register(_ => broken);
        harness.Grain.GameRuntime.Register(_ => healthy);

        // Arenas in a room are independent. An arena that cannot read its balance config must
        // not stop the room's other match from kicking off, and must not throw the failure back
        // at whoever pressed the timer.
        await StartAsync(harness, "broken").ConfigureAwait(true);
        await StartAsync(harness, "healthy").ConfigureAwait(true);

        healthy.Starts.Should().Be(1);
        broken.Starts.Should().Be(0, "a game that could not set its arena up must not then play");
        harness.Grain.GameRuntime.PhaseOf(new GameId("broken")).Should().Be(GamePhase.Idle);
    }

    [Fact]
    public async Task AGameThatThrowsOnRoundEnd_DoesNotStopTheOthers()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);
        RecordingGame broken = new("broken")
        {
            OnRoundEnd = _ => throw new InvalidOperationException("boom"),
        };
        RecordingGame healthy = new("healthy");
        harness.Grain.GameRuntime.Register(_ => broken);
        harness.Grain.GameRuntime.Register(_ => healthy);
        await StartAsync(harness, "broken").ConfigureAwait(true);
        await StartAsync(harness, "healthy").ConfigureAwait(true);

        await EndAsync(harness, "broken").ConfigureAwait(true);
        await EndAsync(harness, "healthy").ConfigureAwait(true);

        // A round left half-ended is worse than one that ended noisily: the survivors must wind down.
        healthy.RoundEnds.Should().Be(1);
    }

    [Fact]
    public async Task ScoringOutsideALiveMatch_IsRefused()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);
        ScoringGame? game = null;
        harness.Grain.GameRuntime.Register(ctx => game = new ScoringGame(ctx));
        await StartAsync(harness).ConfigureAwait(true);
        await EndAsync(harness).ConfigureAwait(true);

        // "A finished game cannot accept score changes" is enforced by the runtime, not remembered
        // by each module.
        await game!.ScoreNowAsync().ConfigureAwait(true);

        harness.Grain.GameRuntime.GetTeamScore(GameTeamColor.Red).Should().Be(5);
    }

    [Fact]
    public async Task AnIdleGameIsNotTicked_UntilItAsksToBe()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);
        RecordingGame game = new("first");
        harness.Grain.GameRuntime.Register(_ => game);

        await harness
            .Grain.GameRuntime.TickAsync(1_000, CancellationToken.None)
            .ConfigureAwait(true);

        // Twenty frames a second per room, in every room in the hotel: "return early when idle" was
        // still a virtual call each time.
        game.Ticks.Should().Be(0);
    }

    [Fact]
    public async Task APlayerLeavingOrEntering_ReachesEveryGame()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);
        RecordingGame first = new("first");
        RecordingGame second = new("second");
        harness.Grain.GameRuntime.Register(_ => first);
        harness.Grain.GameRuntime.Register(_ => second);

        await harness
            .Grain.GameRuntime.OnPlayerLeftAsync(RoomHarness.Stranger, CancellationToken.None)
            .ConfigureAwait(true);
        await harness
            .Grain.GameRuntime.OnPlayerEnteredAsync(RoomHarness.Stranger, CancellationToken.None)
            .ConfigureAwait(true);

        first.PlayersLeft.Should().ContainSingle().Which.Should().Be(RoomHarness.Stranger);
        second.PlayersLeft.Should().ContainSingle().Which.Should().Be(RoomHarness.Stranger);
        first.PlayersEntered.Should().ContainSingle().Which.Should().Be(RoomHarness.Stranger);
    }

    [Fact]
    public async Task APlayerLeavingTheRoom_LeavesTheirTeamWithIt()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);
        await harness
            .Grain.GameRuntime.JoinTeamAsync(
                RoomHarness.Stranger,
                GameTeamColor.Blue,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        await harness
            .Grain.GameRuntime.OnPlayerLeftAsync(RoomHarness.Stranger, CancellationToken.None)
            .ConfigureAwait(true);

        harness.Grain.GameRuntime.GetTeam(RoomHarness.Stranger).Should().Be(GameTeamColor.None);
    }

    [Fact]
    public async Task RoomShutdown_TearsEveryMatchDownThroughItsOwnCleanup()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);
        RecordingGame game = new("first");
        harness.Grain.GameRuntime.Register(_ => game);
        await StartAsync(harness).ConfigureAwait(true);

        await harness.Grain.GameRuntime.ShutdownAsync(CancellationToken.None).ConfigureAwait(true);

        game.Resets.Should().Be(1, "no zombie match may outlive the room activation");
        harness.Grain.GameRuntime.PhaseOf(new GameId("first")).Should().Be(GamePhase.Idle);
    }

    /// <summary>A stand-in game that records what the runtime did to it.</summary>
    private sealed class RecordingGame(string name) : IRoomGame
    {
        public GameProfile Profile { get; } = new() { Id = new GameId(name) };

        public int Starts { get; private set; }

        public int RoundEnds { get; private set; }

        public int Resets { get; private set; }

        public int Ticks { get; private set; }

        public List<GamePhase> Phases { get; } = [];

        public List<MatchId> MatchIds { get; } = [];

        public List<PlayerId> PlayersLeft { get; } = [];

        public List<PlayerId> PlayersEntered { get; } = [];

        /// <summary>How many room events had been published by the time this game was prepared — the
        /// way to observe that the round was announced first.</summary>
        public int RoomEventsSeenAtPrepare { get; private set; }

        public Func<int>? RoomEventCount { get; init; }

        public ArenaValidation Validation { get; init; } = ArenaValidation.Valid;

        public Func<CancellationToken, Task>? OnPrepare { get; init; }

        public Func<CancellationToken, Task>? OnRoundEnd { get; set; }

        public ArenaValidation ValidateArena() => Validation;

        public Task OnPreparingAsync(GameMatch match, CancellationToken ct)
        {
            Phases.Add(GamePhase.Preparing);
            MatchIds.Add(match.Id);
            RoomEventsSeenAtPrepare = RoomEventCount?.Invoke() ?? 0;

            return OnPrepare?.Invoke(ct) ?? Task.CompletedTask;
        }

        public Task OnStartedAsync(GameMatch match, CancellationToken ct)
        {
            Starts++;
            Phases.Add(GamePhase.Running);

            return Task.CompletedTask;
        }

        public Task OnRoundEndingAsync(GameMatch match, CancellationToken ct)
        {
            RoundEnds++;
            Phases.Add(GamePhase.RoundEnding);
            Phases.Add(GamePhase.Finished);

            return OnRoundEnd?.Invoke(ct) ?? Task.CompletedTask;
        }

        public Task OnResettingAsync(GameMatch match, CancellationToken ct)
        {
            Resets++;
            Phases.Add(GamePhase.Resetting);
            Phases.Add(GamePhase.Idle);

            return Task.CompletedTask;
        }

        public Task TickAsync(long nowMs, CancellationToken ct)
        {
            Ticks++;

            return Task.CompletedTask;
        }

        public Task OnSignalAsync(GameSignal signal, CancellationToken ct) => Task.CompletedTask;

        public Task OnParticipantLeftAsync(PlayerId playerId, CancellationToken ct)
        {
            PlayersLeft.Add(playerId);

            return Task.CompletedTask;
        }

        public Task OnParticipantEnteredAsync(PlayerId playerId, CancellationToken ct)
        {
            PlayersEntered.Add(playerId);

            return Task.CompletedTask;
        }
    }

    /// <summary>A game that scores five for red when it starts, and offers a way to try scoring
    /// again once the match is over.</summary>
    private sealed class ScoringGame(IRoomGameContext context) : RoomGameModule(context)
    {
        public override GameProfile Profile { get; } = new() { Id = new GameId("scoring") };

        public override Task OnStartedAsync(GameMatch match, CancellationToken ct) =>
            ScoreAsync(ct);

        public Task ScoreNowAsync() => ScoreAsync(CancellationToken.None);

        private Task ScoreAsync(CancellationToken ct) =>
            _context.ScoreAsync(
                new GameScore(
                    _context.Palette.TeamOf(GameTeamColor.Red),
                    default,
                    5,
                    ScoreReason.Unspecified,
                    default
                ),
                ct
            );
    }
}
