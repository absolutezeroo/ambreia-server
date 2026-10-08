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
using Vortex.Database.Context;
using Vortex.Database.Entities.Rpg;
using Vortex.Database.Tests.Support;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Events.Rpg;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Primitives.Rpg.Characters.Grains;
using Vortex.Rpg.Characters.Configuration;
using Vortex.Rpg.Characters.Grains;
using Vortex.Rpg.Characters.Stats;
using Vortex.Rpg.Content;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Stats;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Database.Tests.Rpg;

/// <summary>The character grain (spec Personnage §7): a row only once a class is chosen, read back
/// on activation, stats from the class at its level.</summary>
public sealed class PlayerCharacterGrainTests : IAsyncLifetime
{
    private const int Player = 7;

    private readonly List<IEvent> _events = [];
    private readonly Dictionary<string, ContentDefinition> _content = new()
    {
        ["class.warrior"] = CharacterContent.Warrior,
        ["spec.warrior.guardian"] = CharacterContent.Guardian,
        ["progression.default"] = CharacterContent.Progression,
        ["class.classless"] = CharacterContent.Classless,
    };

    private SqliteConnection _conn = null!;
    private DbContextOptions<VortexDbContext> _options = null!;
    private RecordingLogger<PlayerCharacterGrain> _logger = new();

    public async Task InitializeAsync() => (_conn, _options) = await SqliteTestDb.OpenAsync();

    public async Task DisposeAsync() => await _conn.DisposeAsync();

    [Fact]
    public async Task AnAvatarWithoutAClass_HasNoSummaryAndNoStats()
    {
        IPlayerCharacterGrain grain = await ActivateAsync();

        (await grain.GetSummaryAsync()).Should().BeNull();
        (await grain.GetStatsAsync()).Should().BeNull();
    }

    [Fact]
    public async Task ChoosingAClass_CreatesTheCharacterAtLevelOne()
    {
        IPlayerCharacterGrain grain = await ActivateAsync();

        (await grain.ChooseClassAsync("class.warrior")).Should().Be(ChooseClassOutcome.Chosen);

        PlayerCharacterEntity row = await RowAsync();
        (row.ClassId, row.SpecializationId, row.Level, row.Xp)
            .Should()
            .Be(("class.warrior", "spec.warrior.guardian", 1, 0));
        _events.OfType<CharacterCreatedEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task ChoosingTwice_IsRefused()
    {
        IPlayerCharacterGrain grain = await ActivateAsync();
        await grain.ChooseClassAsync("class.warrior");

        (await grain.ChooseClassAsync("class.warrior"))
            .Should()
            .Be(ChooseClassOutcome.AlreadyChosen);

        await using VortexDbContext db = new(_options);
        (await db.PlayerCharacters.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task AnUnknownOrRetiredClass_IsRefused()
    {
        _content["class.mage"] = CharacterContent.Warrior with
        {
            Id = "class.mage",
            Retired = true,
        };
        IPlayerCharacterGrain grain = await ActivateAsync();

        (await grain.ChooseClassAsync("class.paladin"))
            .Should()
            .Be(ChooseClassOutcome.UnknownClass);
        (await grain.ChooseClassAsync("class.mage")).Should().Be(ChooseClassOutcome.UnknownClass);
    }

    [Fact]
    public async Task TheStats_AreTheClassAtLevelOne()
    {
        IPlayerCharacterGrain grain = await ActivateAsync();
        await grain.ChooseClassAsync("class.warrior");

        CharacterStatsSnapshot stats = (await grain.GetStatsAsync())!;

        stats.MaxHealth.Should().Be(120);
        stats.Sources.Select(s => s.Source).Should().Equal("class");
    }

    [Fact]
    public async Task TheStarterClass_IsLeftOnceForARealClass()
    {
        IPlayerCharacterGrain grain = await ActivateAsync();
        (await grain.ChooseClassAsync("class.classless")).Should().Be(ChooseClassOutcome.Chosen);

        (await grain.ChooseClassAsync("class.warrior")).Should().Be(ChooseClassOutcome.Chosen);
        (await grain.ChooseClassAsync("class.classless"))
            .Should()
            .Be(ChooseClassOutcome.AlreadyChosen);

        PlayerCharacterEntity row = await RowAsync();
        (row.ClassId, row.SpecializationId).Should().Be(("class.warrior", "spec.warrior.guardian"));
        (await grain.GetStatsAsync())!.MaxHealth.Should().Be(120);
    }

    [Fact]
    public async Task LeavingTheStarterClass_KeepsLevelAndXp()
    {
        await StoreAsync("class.classless", level: 5, xp: 30);
        IPlayerCharacterGrain grain = await ActivateAsync();

        await grain.ChooseClassAsync("class.warrior");

        CharacterSummary summary = (await grain.GetSummaryAsync())!;
        (summary.ClassId, summary.Level, summary.Xp).Should().Be(("class.warrior", 5, 30));
        _events
            .OfType<CharacterClassChangedEvent>()
            .Should()
            .Equal(new CharacterClassChangedEvent(Player, "class.classless", "class.warrior"));
    }

    [Fact]
    public async Task TheStarterClass_IsNotLeftForAnotherStarterOrAnUnknownClass()
    {
        await StoreAsync("class.classless", level: 2);
        IPlayerCharacterGrain grain = await ActivateAsync();

        (await grain.ChooseClassAsync("class.classless"))
            .Should()
            .Be(ChooseClassOutcome.AlreadyChosen);
        (await grain.ChooseClassAsync("class.paladin"))
            .Should()
            .Be(ChooseClassOutcome.UnknownClass);
        (await RowAsync()).ClassId.Should().Be("class.classless");
    }

    [Fact]
    public async Task AStoredCharacter_IsHydrated()
    {
        await StoreAsync("class.warrior", level: 7);

        IPlayerCharacterGrain grain = await ActivateAsync();

        (await grain.GetSummaryAsync())!.Level.Should().Be(7);
    }

    [Fact]
    public async Task AStoredClassUnknownToTheContent_StaysReadable()
    {
        await StoreAsync("class.paladin", level: 3);

        IPlayerCharacterGrain grain = await ActivateAsync();

        (await grain.GetSummaryAsync())!.ClassId.Should().Be("class.paladin");
        CharacterStatsSnapshot stats = (await grain.GetStatsAsync())!;
        (stats.Strength, stats.MaxHealth).Should().Be((0, 0));
        _logger
            .Entries.Should()
            .Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("class.paladin"));
    }

    private async Task<IPlayerCharacterGrain> ActivateAsync()
    {
        PlayerCharacterGrain grain =
            GrainActivationContext.CreateWithIntegerKey<PlayerCharacterGrain>(
                Player,
                new OptionsDbContextFactory(_options),
                CharacterContent.Registry(_content, () => 1),
                new IStatSource[] { new ClassLevelStatSource() },
                FakeProxy.Create<IEventPublisher>(call =>
                {
                    lock (_events)
                    {
                        _events.Add((IEvent)call.Args![0]!);
                    }

                    return null;
                }),
                FakeProxy.Create<ICancellableEventPublisher>(_ =>
                    Task.FromResult(new EventContext { CorrelationId = "test" })
                ),
                Options.Create(new CharacterConfig()),
                FakeProxy.Create<IVortexMetrics>(_ => null),
                _logger,
                SilentPresence.Grains()
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

    private async Task StoreAsync(string classId, int level, int xp = 0)
    {
        await using VortexDbContext db = new(_options);
        db.PlayerCharacters.Add(
            new PlayerCharacterEntity
            {
                PlayerEntityId = Player,
                ClassId = classId,
                SpecializationId = "spec.warrior.guardian",
                Level = level,
                Xp = xp,
            }
        );
        await db.SaveChangesAsync();
    }
}
