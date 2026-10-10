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

/// <summary>
/// An operator's XP grant (audit P0-6): a retried request gives nothing more, a day's grants are
/// capped, and the XP is in the row before the call returns.
/// </summary>
public sealed class CharacterXpGrantTests : IAsyncLifetime
{
    private const int Player = 7;

    private SqliteConnection _conn = null!;
    private DbContextOptions<VortexDbContext> _options = null!;

    public async Task InitializeAsync() => (_conn, _options) = await SqliteTestDb.OpenAsync();

    public async Task DisposeAsync() => await _conn.DisposeAsync();

    [Fact]
    public async Task TheSameKeyTwice_GrantsOnce()
    {
        PlayerCharacterGrain grain = await ActivateAsync();

        XpGrantOutcome first = await grain.GrantXpAsync("ops-1", 50, "admin:1");
        XpGrantOutcome replay = await grain.GrantXpAsync("ops-1", 50, "admin:1");

        (first, replay).Should().Be((XpGrantOutcome.Granted, XpGrantOutcome.Duplicate));
        (await RowAsync()).Xp.Should().Be(50);
    }

    [Fact]
    public async Task AGrant_IsWritten_BeforeTheCallReturns()
    {
        PlayerCharacterGrain grain = await ActivateAsync();

        await grain.GrantXpAsync("ops-2", 50, "admin:1");

        (await RowAsync()).Xp.Should().Be(50, "no timer, no deactivation: a crash loses nothing");
    }

    [Fact]
    public async Task TheDaysGrants_AreCapped()
    {
        PlayerCharacterGrain grain = await ActivateAsync(maxXpPerDay: 120);

        await grain.GrantXpAsync("ops-3", 50, "admin:1");
        await grain.GrantXpAsync("ops-4", 50, "admin:1");
        XpGrantOutcome third = await grain.GrantXpAsync("ops-5", 50, "admin:1");

        third.Should().Be(XpGrantOutcome.DailyCapReached);

        // The XP itself turned into levels; the keys say what the day gave.
        await using VortexDbContext db = new(_options);
        (await db.RpgXpGrants.SumAsync(g => g.Amount)).Should().Be(100);
    }

    [Fact]
    public async Task ARefusedKey_IsNotConsumed()
    {
        PlayerCharacterGrain grain = await ActivateAsync(maxXpPerDay: 60);

        await grain.GrantXpAsync("ops-6", 50, "admin:1");
        XpGrantOutcome capped = await grain.GrantXpAsync("ops-7", 50, "admin:1");

        capped.Should().Be(XpGrantOutcome.DailyCapReached);

        await using VortexDbContext db = new(_options);
        (await db.RpgXpGrants.CountAsync())
            .Should()
            .Be(1, "a grant that gave nothing keeps no key");
    }

    private async Task<PlayerCharacterEntity> RowAsync()
    {
        await using VortexDbContext db = new(_options);

        return await db
            .PlayerCharacters.AsNoTracking()
            .SingleAsync(c => c.PlayerEntityId == Player);
    }

    private async Task<PlayerCharacterGrain> ActivateAsync(int maxXpPerDay = 500_000)
    {
        await using (VortexDbContext db = new(_options))
        {
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
                        ["progression.default"] = CharacterContent.Progression,
                    },
                    () => 1
                ),
                new IStatSource[] { new ClassLevelStatSource() },
                FakeProxy.Create<IEventPublisher>(_ => null),
                FakeProxy.Create<ICancellableEventPublisher>(_ =>
                    Task.FromResult(new EventContext { CorrelationId = "test" })
                ),
                Options.Create(new CharacterConfig { MaxXpPerDay = maxXpPerDay }),
                FakeProxy.Create<IVortexMetrics>(_ => null),
                NullLogger<PlayerCharacterGrain>.Instance,
                SilentPresence.Grains(),
                Options.Create(new ItemConfig())
            );
        await grain.OnActivateAsync(CancellationToken.None);

        return grain;
    }
}
