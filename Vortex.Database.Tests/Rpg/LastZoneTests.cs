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
using Vortex.Primitives.Rooms;
using Vortex.Rpg.Characters.Configuration;
using Vortex.Rpg.Characters.Grains;
using Vortex.Rpg.Characters.Stats;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Stats;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Database.Tests.Rpg;

/// <summary>Spec Personnage §9: the character keeps the world zone it last entered, never a copy.</summary>
public sealed class LastZoneTests : IAsyncLifetime
{
    private const int Player = 7;

    private SqliteConnection _conn = null!;
    private DbContextOptions<VortexDbContext> _options = null!;

    public async Task InitializeAsync() => (_conn, _options) = await SqliteTestDb.OpenAsync();

    public async Task DisposeAsync() => await _conn.DisposeAsync();

    [Fact]
    public async Task AWorldZone_IsKept()
    {
        PlayerCharacterGrain grain = await ActivateAsync(withCharacter: true);

        await grain.SetLastRoomAsync(new RoomId(12));
        await DeactivateAsync(grain);

        (await grain.GetLastRoomAsync()).Should().Be(new RoomId(12));
        (await RowAsync()).LastRoomId.Should().Be(12);
    }

    [Fact]
    public async Task ADungeonCopy_IsNeverKept()
    {
        PlayerCharacterGrain grain = await ActivateAsync(withCharacter: true);
        await grain.SetLastRoomAsync(new RoomId(12));

        await grain.SetLastRoomAsync(new RoomId(1_000_012_001));

        (await grain.GetLastRoomAsync()).Should().Be(new RoomId(12));
    }

    [Fact]
    public async Task WithoutACharacter_NothingIsKept()
    {
        PlayerCharacterGrain grain = await ActivateAsync(withCharacter: false);

        await grain.SetLastRoomAsync(new RoomId(12));

        (await grain.GetLastRoomAsync()).Should().BeNull();
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
                NullLogger<PlayerCharacterGrain>.Instance
            );
        await grain.OnActivateAsync(CancellationToken.None);

        return grain;
    }

    private static Task DeactivateAsync(PlayerCharacterGrain grain) =>
        grain.OnDeactivateAsync(
            new DeactivationReason(DeactivationReasonCode.ShuttingDown, "test"),
            CancellationToken.None
        );

    private async Task<PlayerCharacterEntity> RowAsync()
    {
        await using VortexDbContext db = new(_options);

        return await db
            .PlayerCharacters.AsNoTracking()
            .SingleAsync(c => c.PlayerEntityId == Player);
    }
}
