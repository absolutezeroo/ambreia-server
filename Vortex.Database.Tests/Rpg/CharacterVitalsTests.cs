using System;
using System.Collections.Generic;
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
using Vortex.Primitives.Rpg.Characters;
using Vortex.Rpg.Characters.Configuration;
using Vortex.Rpg.Characters.Grains;
using Vortex.Rpg.Characters.Stats;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Stats;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Database.Tests.Rpg;

/// <summary>Spec M1a §7: a character's health and mana are the same everywhere — kept by the
/// character, written with its progress, regenerated out of any zone, bounded to the maximum.</summary>
public sealed class CharacterVitalsTests : IAsyncLifetime
{
    private const int Player = 7;

    private readonly ManualClock _clock = new(
        new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero)
    );
    private SqliteConnection _conn = null!;
    private DbContextOptions<VortexDbContext> _options = null!;

    public async Task InitializeAsync() => (_conn, _options) = await SqliteTestDb.OpenAsync();

    public async Task DisposeAsync() => await _conn.DisposeAsync();

    [Fact]
    public async Task ANewCharacter_IsFull()
    {
        PlayerCharacterGrain grain = await ActivateAsync(withCharacter: true);

        CharacterCombatProfile profile = (await grain.GetCombatProfileAsync())!;

        profile.Health.Should().Be(profile.Stats.MaxHealth).And.BeGreaterThan(0);
        profile.Mana.Should().Be(profile.Stats.MaxMana);
        (profile.Level, profile.Range, profile.ThreatMultiplier).Should().Be((1, 1, 3));
    }

    [Fact]
    public async Task SetVitals_IsKept_AcrossAReactivation()
    {
        PlayerCharacterGrain grain = await ActivateAsync(withCharacter: true);
        await grain.SetVitalsAsync(10, 5);
        await DeactivateAsync(grain);

        PlayerCharacterGrain again = await ActivateAsync(withCharacter: false);
        CharacterCombatProfile profile = (await again.GetCombatProfileAsync())!;

        (profile.Health, profile.Mana).Should().Be((10, 5));
    }

    [Fact]
    public async Task OutOfZone_HealthRegenerates_FromVitalsAt()
    {
        PlayerCharacterGrain grain = await ActivateAsync(withCharacter: true);
        int max = (await grain.GetCombatProfileAsync())!.Stats.MaxHealth;
        await grain.SetVitalsAsync(10, 0);

        _clock.Now = _clock.Now.AddSeconds(10);

        (await grain.GetCombatProfileAsync())!
            .Health.Should()
            .Be(Math.Min(max, 10 + max * 2 * 10 / 100), "2 % of the max per second, 10 s");

        _clock.Now = _clock.Now.AddHours(1);

        (await grain.GetCombatProfileAsync())!.Health.Should().Be(max);
    }

    [Fact]
    public async Task Health_IsBoundedToTheMaximum()
    {
        PlayerCharacterGrain grain = await ActivateAsync(withCharacter: true);

        await grain.SetVitalsAsync(99_999, 99_999);

        CharacterCombatProfile profile = (await grain.GetCombatProfileAsync())!;
        (profile.Health, profile.Mana)
            .Should()
            .Be((profile.Stats.MaxHealth, profile.Stats.MaxMana));
    }

    /// <summary>Full is kept as "full", not as a number: a maximum that grows later (a level, an
    /// item) finds the character full, not short of the difference.</summary>
    [Fact]
    public async Task FullVitals_AreStoredAsFull()
    {
        PlayerCharacterGrain grain = await ActivateAsync(withCharacter: true);
        CharacterCombatProfile profile = (await grain.GetCombatProfileAsync())!;

        await grain.SetVitalsAsync(profile.Stats.MaxHealth, profile.Stats.MaxMana);
        await DeactivateAsync(grain);

        await using VortexDbContext db = new(_options);
        PlayerCharacterEntity row = await db
            .PlayerCharacters.AsNoTracking()
            .SingleAsync(c => c.PlayerEntityId == Player);
        (row.CurrentHealth, row.CurrentMana).Should().Be((null, null));
    }

    [Fact]
    public async Task WithoutACharacter_TheProfileIsNull()
    {
        PlayerCharacterGrain grain = await ActivateAsync(withCharacter: false);

        (await grain.GetCombatProfileAsync()).Should().BeNull();
    }

    private async Task<PlayerCharacterGrain> ActivateAsync(bool withCharacter)
    {
        if (withCharacter)
        {
            await using VortexDbContext db = new(_options);
            db.PlayerCharacters.Add(
                new PlayerCharacterEntity
                {
                    PlayerEntityId = Player,
                    ClassId = "class.warrior",
                    SpecializationId = "spec.warrior.guardian",
                }
            );
            await db.SaveChangesAsync();
        }

        PlayerCharacterGrain grain =
            GrainActivationContext.CreateWithIntegerKey<PlayerCharacterGrain>(
                Player,
                new OptionsDbContextFactory(_options),
                CharacterContent.Registry(
                    new Dictionary<string, ContentDefinition>
                    {
                        ["class.warrior"] = CharacterContent.Warrior,
                    },
                    () => 1
                ),
                new IStatSource[] { new ClassLevelStatSource() },
                FakeProxy.Create<IEventPublisher>(_ => null),
                FakeProxy.Create<ICancellableEventPublisher>(_ =>
                    Task.FromResult(new EventContext { CorrelationId = "test" })
                ),
                Options.Create(new CharacterConfig()),
                FakeProxy.Create<IVortexMetrics>(_ => null),
                NullLogger<PlayerCharacterGrain>.Instance,
                SilentPresence.Grains(),
                Options.Create(new ItemConfig())
            );
        grain.Time = _clock;
        await grain.OnActivateAsync(CancellationToken.None);

        return grain;
    }

    private static Task DeactivateAsync(PlayerCharacterGrain grain) =>
        grain.OnDeactivateAsync(
            new DeactivationReason(DeactivationReasonCode.ShuttingDown, "test"),
            CancellationToken.None
        );
}
