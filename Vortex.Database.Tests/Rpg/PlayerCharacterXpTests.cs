using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Entities.Rpg;
using Vortex.Database.Tests.Support;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Events.Rpg;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Rpg.Characters.Configuration;
using Vortex.Rpg.Characters.Grains;
using Vortex.Rpg.Characters.Stats;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Stats;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Database.Tests.Rpg;

/// <summary>Spec Personnage §5: XP only through GainXpAsync, levels chained, written at once.</summary>
public sealed class PlayerCharacterXpTests : IAsyncLifetime
{
    private const int Player = 7;

    private readonly List<IEvent> _events = [];
    private readonly RecordingLogger<PlayerCharacterGrain> _logger = new();
    private SqliteConnection _conn = null!;
    private DbContextOptions<VortexDbContext> _options = null!;
    private bool _cancel;
    private Action<XpGainingEvent>? _modify;
    private bool _withProgression = true;
    private readonly List<int> _levelStoredAtLevelUp = [];

    public async Task InitializeAsync() => (_conn, _options) = await SqliteTestDb.OpenAsync();

    public async Task DisposeAsync() => await _conn.DisposeAsync();

    [Fact]
    public async Task AGrant_AddsXpWithoutWriting()
    {
        PlayerCharacterGrain grain = await WarriorAsync(level: 1);

        (await grain.GainXpAsync(50, "test")).Should().Be(XpGrantOutcome.Granted);

        (await grain.GetSummaryAsync())!.Xp.Should().Be(50);
        (await RowAsync()).Xp.Should().Be(0);
    }

    [Fact]
    public async Task ReachingTheThreshold_LevelsUpAndWritesAtOnce()
    {
        PlayerCharacterGrain grain = await WarriorAsync(level: 1);

        await grain.GainXpAsync(100, "test");

        ((await grain.GetSummaryAsync())!.Level, (await RowAsync()).Level).Should().Be((2, 2));
        _events
            .OfType<LevelUpEvent>()
            .Should()
            .Equal(new LevelUpEvent(Player, "class.warrior", 1, 2));
    }

    [Fact]
    public async Task AGrantOverSeveralLevels_StopsAtTheMax()
    {
        PlayerCharacterGrain grain = await WarriorAsync(level: 1);

        (await grain.GainXpAsync(100_000, "test")).Should().Be(XpGrantOutcome.Granted);

        CharacterSummary summary = (await grain.GetSummaryAsync())!;
        (summary.Level, summary.Xp).Should().Be((20, 0));
        _events.OfType<LevelUpEvent>().Should().HaveCount(19);
        (await RowAsync()).Level.Should().Be(20);
    }

    [Fact]
    public async Task AtTheMaxLevel_XpDoesNotAccumulate()
    {
        PlayerCharacterGrain grain = await WarriorAsync(level: 20);

        (await grain.GainXpAsync(50, "test")).Should().Be(XpGrantOutcome.AtMaxLevel);

        (await grain.GetSummaryAsync())!.Xp.Should().Be(0);
    }

    [Fact]
    public async Task ACancelledGain_ChangesNothing()
    {
        PlayerCharacterGrain grain = await WarriorAsync(level: 1);
        _cancel = true;

        (await grain.GainXpAsync(50, "test")).Should().Be(XpGrantOutcome.Cancelled);

        (await grain.GetSummaryAsync())!.Xp.Should().Be(0);
        _events.OfType<XpGainedEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task AModifiedAmount_IsTheOneGranted()
    {
        PlayerCharacterGrain grain = await WarriorAsync(level: 1);
        _modify = e => e.Amount = 80;

        await grain.GainXpAsync(50, "test");

        (await grain.GetSummaryAsync())!.Xp.Should().Be(80);
        _events.OfType<XpGainedEvent>().Single().Amount.Should().Be(80);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(100_001)]
    public async Task ZeroNegativeOrOverTheCap_IsRefusedAndLogged(int amount)
    {
        PlayerCharacterGrain grain = await WarriorAsync(level: 1);

        (await grain.GainXpAsync(amount, "test")).Should().Be(XpGrantOutcome.Refused);

        _logger.Entries.Should().Contain(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task AModifiedAmountOverTheCap_IsRefused()
    {
        PlayerCharacterGrain grain = await WarriorAsync(level: 1);
        _modify = e => e.Amount = int.MaxValue;

        (await grain.GainXpAsync(50, "test")).Should().Be(XpGrantOutcome.Refused);

        (await grain.GetSummaryAsync())!.Xp.Should().Be(0);
    }

    [Fact]
    public async Task ALevelUp_IsPublishedOnlyOnceWritten()
    {
        PlayerCharacterGrain grain = await WarriorAsync(level: 1);

        await grain.GainXpAsync(250, "test");

        _levelStoredAtLevelUp.Should().Equal(3, 3);
    }

    [Fact]
    public async Task WithoutTheProgression_AGrantIsRefusedAndLogged()
    {
        _withProgression = false;
        PlayerCharacterGrain grain = await WarriorAsync(level: 1);

        (await grain.GainXpAsync(50, "test")).Should().Be(XpGrantOutcome.Refused);

        _logger
            .Entries.Should()
            .Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("progression.default"));
    }

    [Fact]
    public async Task WithoutACharacter_NothingHappens()
    {
        PlayerCharacterGrain grain = await ActivateAsync();

        (await grain.GainXpAsync(50, "test")).Should().Be(XpGrantOutcome.NoCharacter);
    }

    [Fact]
    public async Task AFlush_WritesTheXp()
    {
        PlayerCharacterGrain grain = await WarriorAsync(level: 1);
        await grain.GainXpAsync(50, "test");

        await grain.OnDeactivateAsync(
            new DeactivationReason(DeactivationReasonCode.ShuttingDown, "test"),
            CancellationToken.None
        );

        (await RowAsync()).Xp.Should().Be(50);
    }

    private async Task<PlayerCharacterGrain> WarriorAsync(int level)
    {
        await using (VortexDbContext db = new(_options))
        {
            db.PlayerCharacters.Add(
                new PlayerCharacterEntity
                {
                    PlayerEntityId = Player,
                    ClassId = "class.warrior",
                    SpecializationId = "spec.warrior.guardian",
                    Level = level,
                }
            );
            await db.SaveChangesAsync();
        }

        return await ActivateAsync();
    }

    private async Task<PlayerCharacterGrain> ActivateAsync()
    {
        PlayerCharacterGrain grain =
            GrainActivationContext.CreateWithIntegerKey<PlayerCharacterGrain>(
                Player,
                new OptionsDbContextFactory(_options),
                CharacterContent.Registry(
                    _withProgression
                        ? new Dictionary<string, ContentDefinition>
                        {
                            ["class.warrior"] = CharacterContent.Warrior,
                            ["progression.default"] = CharacterContent.Progression,
                        }
                        : new Dictionary<string, ContentDefinition>
                        {
                            ["class.warrior"] = CharacterContent.Warrior,
                        },
                    () => 1
                ),
                new IStatSource[] { new ClassLevelStatSource() },
                FakeProxy.Create<IEventPublisher>(call =>
                {
                    lock (_events)
                    {
                        _events.Add((IEvent)call.Args![0]!);
                    }

                    // What the table held when a LevelUp went out: consumers must never see a
                    // level that a crash could still take back.
                    if (call.Args![0] is LevelUpEvent)
                    {
                        using VortexDbContext db = new(_options);
                        _levelStoredAtLevelUp.Add(
                            db.PlayerCharacters.AsNoTracking()
                                .Single(c => c.PlayerEntityId == Player)
                                .Level
                        );
                    }

                    return Task.CompletedTask;
                }),
                FakeProxy.Create<ICancellableEventPublisher>(call =>
                {
                    _modify?.Invoke((XpGainingEvent)call.Args![0]!);

                    return Task.FromResult(
                        new EventContext { CorrelationId = "test", Cancel = _cancel }
                    );
                }),
                Options.Create(new CharacterConfig()),
                FakeProxy.Create<IVortexMetrics>(_ => null),
                _logger
            );
        await grain.OnActivateAsync(CancellationToken.None);

        return grain;
    }

    private async Task<PlayerCharacterEntity> RowAsync()
    {
        await using VortexDbContext db = new(_options);

        return await db
            .PlayerCharacters.AsNoTracking()
            .SingleAsync(c => c.PlayerEntityId == Player);
    }
}
