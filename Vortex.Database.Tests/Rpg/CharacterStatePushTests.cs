using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Entities.Rpg;
using Vortex.Database.Tests.Support;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Protocol.Messages.Outgoing.Rpg.Characters;
using Vortex.Rpg.Characters.Configuration;
using Vortex.Rpg.Characters.Grains;
using Vortex.Rpg.Characters.Stats;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Stats;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Database.Tests.Rpg;

/// <summary>The character's state pushed to its player (spec F3a §5.6, §9): the whole state on
/// entering a zone, progress after a gain, stats before it on a level up.</summary>
public sealed class CharacterStatePushTests : IAsyncLifetime
{
    private const int PLAYER_ID = 7;

    private readonly List<object> _sent = [];
    private readonly Dictionary<string, ContentDefinition> _content = new()
    {
        ["class.warrior"] = CharacterContent.Warrior,
        ["spec.warrior.guardian"] = CharacterContent.Guardian,
        ["progression.default"] = CharacterContent.Progression,
        ["class.classless"] = CharacterContent.Classless,
    };

    private SqliteConnection _conn = null!;
    private DbContextOptions<VortexDbContext> _options = null!;

    public async Task InitializeAsync() => (_conn, _options) = await SqliteTestDb.OpenAsync();

    public async Task DisposeAsync() => await _conn.DisposeAsync();

    [Fact]
    public async Task EnteringARoom_PushesTheState()
    {
        await StoreAsync(level: 1);
        PlayerCharacterGrain grain = await ActivateAsync();

        await grain.EnteredRoomAsync(new RoomId(12));

        RpgCharacterStateMessageComposer state = _sent
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<RpgCharacterStateMessageComposer>()
            .Subject;
        (state.PlayerId, state.ClassId, state.Level, state.MaxLevel, state.Xp, state.XpToNext)
            .Should()
            .Be((PLAYER_ID, "class.warrior", 1, 20, 0, 100));
        state.Stats.MaxHealth.Should().Be(120);
        state.Stats.CritPercent.Should().BeApproximately(6.2, 1e-9);
    }

    [Fact]
    public async Task AGain_PushesProgress()
    {
        await StoreAsync(level: 1);
        PlayerCharacterGrain grain = await ActivateAsync();

        await grain.GainXpAsync(50, "test");

        _sent
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(
                new RpgCharacterProgressMessageComposer
                {
                    Level = 1,
                    Xp = 50,
                    XpToNext = 100,
                }
            );
    }

    [Fact]
    public async Task ALevelUp_PushesStatsThenProgress()
    {
        await StoreAsync(level: 1);
        PlayerCharacterGrain grain = await ActivateAsync();

        await grain.GainXpAsync(120, "test");

        _sent
            .Select(c => c.GetType())
            .Should()
            .Equal(
                typeof(RpgCharacterStatsMessageComposer),
                typeof(RpgCharacterProgressMessageComposer)
            );
        ((RpgCharacterStatsMessageComposer)_sent[0]).Stats.MaxHealth.Should().BeGreaterThan(120);
        _sent[1]
            .Should()
            .BeEquivalentTo(
                new RpgCharacterProgressMessageComposer
                {
                    Level = 2,
                    Xp = 20,
                    XpToNext = 150,
                }
            );
    }

    /// <summary>
    /// The class can be chosen on the character screen of a second tab while the character plays in
    /// the first: its HUD must not keep the starter's stats (spec F3a §5.6, §9).
    /// </summary>
    [Fact]
    public async Task LeavingTheStarterClass_PushesTheNewStats()
    {
        await StoreAsync(level: 2, classId: "class.classless");
        PlayerCharacterGrain grain = await ActivateAsync();

        await grain.ChooseClassAsync("class.warrior");

        RpgCharacterStatsMessageComposer stats = _sent
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<RpgCharacterStatsMessageComposer>()
            .Subject;
        stats.Stats.Strength.Should().Be(15, "a level 2 warrior: 12 + 3");
    }

    [Fact]
    public async Task WithoutACharacter_NothingIsPushed()
    {
        PlayerCharacterGrain grain = await ActivateAsync();

        await grain.EnteredRoomAsync(new RoomId(12));
        await grain.PushStateAsync();

        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task AtTheMaxLevel_XpToNextIsZero()
    {
        await StoreAsync(level: 20);
        PlayerCharacterGrain grain = await ActivateAsync();

        await grain.PushStateAsync();

        RpgCharacterStateMessageComposer state = (RpgCharacterStateMessageComposer)_sent.Single();
        (state.Level, state.MaxLevel, state.XpToNext).Should().Be((20, 20, 0));
    }

    /// <summary>
    /// The level is written before the push: a stat source failing then must not turn the grant into
    /// an error, or a retry would grant the XP twice.
    /// </summary>
    [Fact]
    public async Task AStatSourceFailingOnALevelUp_StillGrantsAndPushesProgress()
    {
        await StoreAsync(level: 1);
        IStatSource broken = FakeProxy.Create<IStatSource>(call =>
            call.Method.Name == "get_Name"
                ? "broken"
                : throw new InvalidOperationException("stat source down")
        );
        PlayerCharacterGrain grain = await ActivateAsync(broken);

        (await grain.GainXpAsync(120, "test")).Should().Be(XpGrantOutcome.Granted);

        _sent
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<RpgCharacterProgressMessageComposer>();
    }

    private async Task<PlayerCharacterGrain> ActivateAsync(IStatSource? extraSource = null)
    {
        PlayerCharacterGrain grain =
            GrainActivationContext.CreateWithIntegerKey<PlayerCharacterGrain>(
                PLAYER_ID,
                new OptionsDbContextFactory(_options),
                CharacterContent.Registry(_content, () => 1),
                extraSource is null
                    ? new IStatSource[] { new ClassLevelStatSource() }
                    : new IStatSource[] { new ClassLevelStatSource(), extraSource },
                FakeProxy.Create<IEventPublisher>(_ => null),
                FakeProxy.Create<ICancellableEventPublisher>(_ =>
                    Task.FromResult(new EventContext { CorrelationId = "test" })
                ),
                Options.Create(new CharacterConfig()),
                FakeProxy.Create<IVortexMetrics>(_ => null),
                NullLogger<PlayerCharacterGrain>.Instance,
                Presence(),
                Options.Create(new ItemConfig())
            );
        await grain.OnActivateAsync(CancellationToken.None);

        return grain;
    }

    private IGrainFactory Presence() =>
        FakeProxy.Create<IGrainFactory>(call =>
            call.Method.Name == nameof(IGrainFactory.GetGrain)
                ? FakeProxy.Create<IPlayerPresenceGrain>(inner =>
                {
                    if (inner.Method.Name == nameof(IPlayerPresenceGrain.SendComposerAsync))
                    {
                        _sent.AddRange(inner.Args![0] is object[] many ? many : [inner.Args[0]!]);
                    }

                    return null;
                })
                : null
        );

    private async Task StoreAsync(int level, string classId = "class.warrior")
    {
        await using VortexDbContext db = new(_options);
        db.PlayerCharacters.Add(
            new PlayerCharacterEntity
            {
                PlayerEntityId = PLAYER_ID,
                ClassId = classId,
                SpecializationId =
                    classId == "class.warrior"
                        ? "spec.warrior.guardian"
                        : "spec.classless.adventurer",
                Level = level,
                Xp = 0,
            }
        );
        await db.SaveChangesAsync();
    }
}
