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
using Vortex.Primitives.Rpg.Characters;
using Vortex.Primitives.Rpg.Items;
using Vortex.Primitives.Rpg.Items.Enums;
using Vortex.Protocol.Messages.Outgoing.Rpg.Characters;
using Vortex.Protocol.Messages.Outgoing.Rpg.Items;
using Vortex.Rpg.Characters.Configuration;
using Vortex.Rpg.Characters.Grains;
using Vortex.Rpg.Characters.Stats;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Stats;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Database.Tests.Rpg;

/// <summary>Spec Objets §17: the player is told how each of its operations ended, and sees the
/// whole bag after every change and on entering a zone.</summary>
public sealed class CharacterItemsPushTests : IAsyncLifetime
{
    private const int Player = 7;

    private readonly List<object> _sent = [];
    private readonly List<CharacterStatsSnapshot> _statsForwarded = [];
    private readonly Dictionary<string, ContentDefinition> _content = new()
    {
        ["class.warrior"] = CharacterContent.Warrior,
        ["spec.warrior.guardian"] = CharacterContent.Guardian,
        ["progression.default"] = CharacterContent.Progression,
        [ItemContent.Sword.Id] = ItemContent.Sword,
        [ItemContent.Shard.Id] = ItemContent.Shard,
    };

    private SqliteConnection _conn = null!;
    private DbContextOptions<VortexDbContext> _options = null!;

    public async Task InitializeAsync() => (_conn, _options) = await SqliteTestDb.OpenAsync();

    public async Task DisposeAsync() => await _conn.DisposeAsync();

    [Fact]
    public async Task AMove_IsAnsweredThenTheBagIsSent()
    {
        PlayerCharacterGrain grain = await WarriorWithAsync(ItemContent.Shard, 2);

        await grain.MoveItemAsync(0, 1);

        _sent
            .Select(Describe)
            .Should()
            .Equal("result:1:ok", $"inventory:30:50:{ItemContent.Shard.Id}@1");
    }

    [Fact]
    public async Task ARefusal_IsAnsweredAlone()
    {
        PlayerCharacterGrain grain = await WarriorWithAsync(ItemContent.Shard, 2);

        await grain.MoveItemAsync(5, 1);

        _sent.Select(Describe).Should().Equal("result:1:not_found");
    }

    [Fact]
    public async Task AnEquip_AlsoSendsTheStats()
    {
        PlayerCharacterGrain grain = await WarriorWithAsync(ItemContent.Sword, 1);

        await grain.EquipItemAsync(0);

        _sent
            .Select(Describe)
            .Should()
            .Equal(
                "stats:15",
                "result:3:ok",
                $"inventory:30:50:{ItemContent.Sword.Id}@{(int)EquipmentSlot.Weapon}"
            );
    }

    /// <summary>Spec M1a §4.4: new stats reach the zone the player is in, through his presence.</summary>
    [Fact]
    public async Task AnEquip_TellsThePresenceTheNewStats()
    {
        PlayerCharacterGrain grain = await WarriorWithAsync(ItemContent.Sword, 1);

        await grain.EquipItemAsync(0);

        _statsForwarded.Should().ContainSingle().Which.Strength.Should().Be(15);
    }

    /// <summary>Spec M1a §8.1: dying wears the equipment; the bag shows the new durability.</summary>
    [Fact]
    public async Task AWear_SendsTheBag()
    {
        PlayerCharacterGrain grain = await WarriorWithAsync(ItemContent.Sword, 1);
        await grain.EquipItemAsync(0);
        _sent.Clear();

        await grain.WearEquipmentAsync(10);

        _sent
            .Select(Describe)
            .Should()
            .Contain($"inventory:30:50:{ItemContent.Sword.Id}@{(int)EquipmentSlot.Weapon}");
    }

    [Fact]
    public async Task AGrant_SendsTheBag()
    {
        PlayerCharacterGrain grain = await WarriorWithAsync(ItemContent.Shard, 2);

        await grain.GrantItemsAsync(
            "k2",
            [new ItemGrantLine { DefinitionId = ItemContent.Sword.Id, Quantity = 1 }]
        );

        _sent
            .Select(Describe)
            .Should()
            .Equal($"inventory:30:50:{ItemContent.Shard.Id}@0,{ItemContent.Sword.Id}@1");
    }

    [Fact]
    public async Task TheState_IsFollowedByTheBag()
    {
        PlayerCharacterGrain grain = await WarriorWithAsync(ItemContent.Shard, 2);

        await grain.PushStateAsync();

        _sent.Select(Describe).Should().Equal("state", $"inventory:30:50:{ItemContent.Shard.Id}@0");
    }

    private static string Describe(object composer) =>
        composer switch
        {
            RpgItemResultMessageComposer r => $"result:{r.Action}:{r.Code}",
            RpgInventoryMessageComposer i =>
                $"inventory:{i.BagSize}:{i.MaxParcels}:{string.Join(',', i.Items.Select(x => $"{x.DefinitionId}@{x.Slot}"))}",
            RpgCharacterStatsMessageComposer s => $"stats:{s.Stats.Strength}",
            RpgCharacterStateMessageComposer => "state",
            _ => composer.GetType().Name,
        };

    /// <summary>A warrior holding <paramref name="quantity"/> of <paramref name="definition"/> in
    /// case 0, already written; what the activation pushed is forgotten.</summary>
    private async Task<PlayerCharacterGrain> WarriorWithAsync(
        ItemDefinition definition,
        int quantity
    )
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
            db.RpgItems.Add(
                new RpgItemEntity
                {
                    PlayerEntityId = Player,
                    DefinitionId = definition.Id,
                    Quantity = quantity,
                    Durability = definition.MaxDurability > 0 ? definition.MaxDurability : null,
                    Container = ItemContainer.Bag,
                    Slot = 0,
                }
            );
            await db.SaveChangesAsync();
        }

        PlayerCharacterGrain grain =
            GrainActivationContext.CreateWithIntegerKey<PlayerCharacterGrain>(
                Player,
                new OptionsDbContextFactory(_options),
                CharacterContent.Registry(_content, () => 1),
                new IStatSource[] { new ClassLevelStatSource(), new EquipmentStatSource() },
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
        _sent.Clear();

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

                    if (
                        inner.Method.Name
                        == nameof(IPlayerPresenceGrain.OnCharacterStatsChangedAsync)
                    )
                    {
                        _statsForwarded.Add((CharacterStatsSnapshot)inner.Args![0]!);
                    }

                    return null;
                })
                : null
        );
}
