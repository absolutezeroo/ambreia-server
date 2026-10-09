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
using Vortex.Primitives.Rpg.Items;
using Vortex.Primitives.Rpg.Items.Enums;
using Vortex.Rpg.Characters.Configuration;
using Vortex.Rpg.Characters.Grains;
using Vortex.Rpg.Characters.Stats;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Stats;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Database.Tests.Rpg;

/// <summary>Spec Objets §6, §7, §11: the items of a character, owned by its grain — a grant written
/// at once with its key, the rest by the flush, and all of it read back as it was.</summary>
public sealed class CharacterItemsTests : IAsyncLifetime
{
    private const int Player = 7;

    private readonly List<IEvent> _events = [];
    private readonly Dictionary<string, ContentDefinition> _content = new()
    {
        ["class.warrior"] = CharacterContent.Warrior,
        ["spec.warrior.guardian"] = CharacterContent.Guardian,
        ["progression.default"] = CharacterContent.Progression,
        [ItemContent.Sword.Id] = ItemContent.Sword,
        [ItemContent.Shard.Id] = ItemContent.Shard,
        [ItemContent.NewShard.Id] = ItemContent.NewShard,
    };

    private readonly CommitFaults _faults = new();
    private readonly FailingSaveInterceptor _saveFaults = new();
    private readonly RecordingLogger<PlayerCharacterGrain> _logger = new();
    private SqliteConnection _conn = null!;
    private DbContextOptions<VortexDbContext> _options = null!;
    private long _contentVersion = 1;

    public async Task InitializeAsync() => (_conn, _options) = await SqliteTestDb.OpenAsync();

    public async Task DisposeAsync() => await _conn.DisposeAsync();

    [Fact]
    public async Task WithoutACharacter_EveryOperationIsNoCharacter()
    {
        PlayerCharacterGrain grain = await ActivateAsync();

        (await grain.GrantItemsAsync("k", [Line(ItemContent.Sword, 1)]))
            .Outcome.Should()
            .Be(ItemOutcome.NoCharacter);
        (await grain.MoveItemAsync(0, 1)).Should().Be(ItemOutcome.NoCharacter);
        (await grain.SplitItemAsync(0, 1, 1)).Should().Be(ItemOutcome.NoCharacter);
        (await grain.EquipItemAsync(0)).Should().Be(ItemOutcome.NoCharacter);
        (await grain.UnequipItemAsync(5)).Should().Be(ItemOutcome.NoCharacter);
        (await grain.DestroyItemAsync(0, 1)).Should().Be(ItemOutcome.NoCharacter);
        (await grain.ClaimParcelAsync(0)).Should().Be(ItemOutcome.NoCharacter);
        (await grain.RemoveItemsAsync(ItemContent.Shard.Id, 1))
            .Should()
            .Be(ItemOutcome.NoCharacter);
        (await grain.GetItemsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task AGrant_IsWrittenAtOnceWithItsKey()
    {
        PlayerCharacterGrain grain = await WarriorAsync();

        ItemGrantResult result = await grain.GrantItemsAsync("k1", [Line(ItemContent.Sword, 1)]);

        (result.Outcome, result.ToBag).Should().Be((ItemOutcome.Ok, 1));
        RpgItemEntity row = (await RowsAsync()).Single();
        (row.DefinitionId, row.Quantity, row.Durability, row.Container, row.Slot)
            .Should()
            .Be((ItemContent.Sword.Id, 1, 50, ItemContainer.Bag, 0));
        await using VortexDbContext db = new(_options);
        (await db.RpgItemGrants.SingleAsync()).GrantKey.Should().Be("k1");
        _events
            .OfType<ItemGrantedEvent>()
            .Should()
            .ContainSingle(e => e.ItemId == row.Id && e.GrantKey == "k1");
    }

    [Fact]
    public async Task AReplayedKey_GrantsNothing()
    {
        PlayerCharacterGrain grain = await WarriorAsync();
        await grain.GrantItemsAsync("k1", [Line(ItemContent.Sword, 1)]);

        (await grain.GrantItemsAsync("k1", [Line(ItemContent.Sword, 1)]))
            .Outcome.Should()
            .Be(ItemOutcome.DuplicateGrant);

        (await RowsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task AFullBag_SendsToAParcel_ThenTheParcelIsClaimed()
    {
        PlayerCharacterGrain grain = await WarriorAsync();
        await grain.GrantItemsAsync("k1", [Line(ItemContent.Sword, 2)]);

        ItemGrantResult result = await grain.GrantItemsAsync("k2", [Line(ItemContent.Shard, 3)]);

        (result.Outcome, result.ToParcels).Should().Be((ItemOutcome.Ok, 3));
        (await grain.DestroyItemAsync(0, 1)).Should().Be(ItemOutcome.Ok);
        (await grain.ClaimParcelAsync(0)).Should().Be(ItemOutcome.Ok);
        CharacterItemSnapshot shard = (await grain.GetItemsAsync()).Single(i =>
            i.DefinitionId == ItemContent.Shard.Id
        );
        (shard.Container, shard.Slot, shard.Quantity).Should().Be((ItemContainer.Bag, 0, 3));
    }

    [Fact]
    public async Task FullParcels_GrantNothingAndConsumeNoKey()
    {
        PlayerCharacterGrain grain = await WarriorAsync();
        await grain.GrantItemsAsync("k1", [Line(ItemContent.Sword, 3)]);

        (await grain.GrantItemsAsync("k2", [Line(ItemContent.Sword, 1)]))
            .Outcome.Should()
            .Be(ItemOutcome.ParcelsFull);

        (await RowsAsync()).Should().HaveCount(3);
        await using VortexDbContext db = new(_options);
        (await db.RpgItemGrants.AnyAsync(g => g.GrantKey == "k2")).Should().BeFalse();
    }

    [Fact]
    public async Task AGrantWhoseWriteFails_LeavesTheBagAndTheKeyFree()
    {
        PlayerCharacterGrain grain = await WarriorAsync();
        _faults.FailNext = 1;

        Func<Task> grant = () => grain.GrantItemsAsync("k1", [Line(ItemContent.Shard, 5)]);

        await grant.Should().ThrowAsync<Exception>();
        (await grain.GetItemsAsync()).Should().BeEmpty();
        (await RowsAsync()).Should().BeEmpty();
        (await grain.GrantItemsAsync("k1", [Line(ItemContent.Shard, 5)]))
            .Outcome.Should()
            .Be(ItemOutcome.Ok);
    }

    [Fact]
    public async Task Equipping_RaisesTheStats_AndAllSurvivesReactivation()
    {
        PlayerCharacterGrain grain = await WarriorAsync();
        await grain.GrantItemsAsync("k1", [Line(ItemContent.Sword, 1)]);

        (await grain.EquipItemAsync(0)).Should().Be(ItemOutcome.Ok);

        (await grain.GetStatsAsync())!.Strength.Should().Be(15);
        (await grain.GetStatsAsync())!
            .Sources.Select(s => s.Source)
            .Should()
            .Equal("class", "equipment");
        CharacterItemSnapshot[] before = await grain.GetItemsAsync();

        await DeactivateAsync(grain);
        PlayerCharacterGrain again = await ActivateAsync();

        (await again.GetItemsAsync()).Should().Equal(before);
        (await again.GetStatsAsync())!.Strength.Should().Be(15);
        _events.OfType<CharacterStatsChangedEvent>().Should().NotBeEmpty();
    }

    [Fact]
    public async Task AMove_IsWrittenByTheFlush_NotAtOnce()
    {
        PlayerCharacterGrain grain = await WarriorAsync();
        await grain.GrantItemsAsync("k1", [Line(ItemContent.Shard, 2)]);

        (await grain.MoveItemAsync(0, 1)).Should().Be(ItemOutcome.Ok);

        (await RowsAsync()).Single().Slot.Should().Be(0);
        await DeactivateAsync(grain);
        (await RowsAsync()).Single().Slot.Should().Be(1);
    }

    [Fact]
    public async Task ASplitNotYetFlushed_IsWrittenAtDeactivation()
    {
        PlayerCharacterGrain grain = await WarriorAsync();
        await grain.GrantItemsAsync("k1", [Line(ItemContent.Shard, 20)]);

        (await grain.SplitItemAsync(0, 5, 1)).Should().Be(ItemOutcome.Ok);
        await DeactivateAsync(grain);

        (await RowsAsync())
            .Select(r => (r.Slot, r.Quantity))
            .OrderBy(x => x.Slot)
            .Should()
            .Equal((0, 15), (1, 5));
        PlayerCharacterGrain again = await ActivateAsync();
        (await again.GetItemsAsync()).Select(i => i.Quantity).Should().Equal(15, 5);
    }

    [Fact]
    public async Task ASplitPartDestroyedBeforeTheFlush_LeavesNoRow()
    {
        PlayerCharacterGrain grain = await WarriorAsync();
        await grain.GrantItemsAsync("k1", [Line(ItemContent.Shard, 20)]);
        await grain.SplitItemAsync(0, 5, 1);

        (await grain.DestroyItemAsync(1, 5)).Should().Be(ItemOutcome.Ok);
        await DeactivateAsync(grain);

        (await RowsAsync()).Should().ContainSingle().Which.Quantity.Should().Be(15);
    }

    [Fact]
    public async Task AReplacedDefinition_IsReReadAndNeverWrittenBack()
    {
        PlayerCharacterGrain grain = await WarriorAsync();
        await grain.GrantItemsAsync("k1", [Line(ItemContent.Shard, 2)]);
        await using (VortexDbContext db = new(_options))
        {
            await db
                .RpgItems.Where(i => i.DefinitionId == ItemContent.Shard.Id)
                .ExecuteUpdateAsync(up =>
                    up.SetProperty(i => i.DefinitionId, ItemContent.NewShard.Id)
                );
        }
        _contentVersion = 2;

        (await grain.MoveItemAsync(0, 1)).Should().Be(ItemOutcome.Ok);

        (await grain.GetItemsAsync()).Single().DefinitionId.Should().Be(ItemContent.NewShard.Id);
        await DeactivateAsync(grain);
        RpgItemEntity row = (await RowsAsync()).Single();
        (row.DefinitionId, row.Slot).Should().Be((ItemContent.NewShard.Id, 1));
    }

    [Fact]
    public async Task ACopyOfAnUnknownDefinition_StaysWithoutStats()
    {
        await StoreWarriorAsync();
        await using (VortexDbContext db = new(_options))
        {
            db.RpgItems.Add(
                new RpgItemEntity
                {
                    PlayerEntityId = Player,
                    DefinitionId = "item.weapon.gone",
                    Quantity = 1,
                    Container = ItemContainer.Equipment,
                    Slot = (int)EquipmentSlot.Weapon,
                }
            );
            await db.SaveChangesAsync();
        }

        PlayerCharacterGrain grain = await ActivateAsync();

        (await grain.GetItemsAsync()).Should().ContainSingle();
        (await grain.GetStatsAsync())!.Strength.Should().Be(12);
        _logger
            .Entries.Should()
            .Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("item.weapon.gone"));
    }

    /// <summary>A part split off and not written yet holds the definition id it was split with: an
    /// import that replaces it must move the part too, or the flush inserts a removed id.</summary>
    [Fact]
    public async Task AReplacedDefinition_ReachesAPartNotYetWritten()
    {
        PlayerCharacterGrain grain = await WarriorAsync();
        await grain.GrantItemsAsync("k1", [Line(ItemContent.Shard, 20)]);
        await grain.SplitItemAsync(0, 5, 1);
        await using (VortexDbContext db = new(_options))
        {
            await db
                .RpgItems.Where(i => i.DefinitionId == ItemContent.Shard.Id)
                .ExecuteUpdateAsync(up =>
                    up.SetProperty(i => i.DefinitionId, ItemContent.NewShard.Id)
                );
        }
        _contentVersion = 2;

        await DeactivateAsync(grain);

        (await RowsAsync())
            .Select(r => r.DefinitionId)
            .Should()
            .Equal(ItemContent.NewShard.Id, ItemContent.NewShard.Id);
    }

    /// <summary>A destroy is written before it counts: a database that fails then leaves the copy
    /// whole, and nothing is audited.</summary>
    [Fact]
    public async Task ADestroyWhoseWriteFails_DestroysNothing()
    {
        PlayerCharacterGrain grain = await WarriorAsync();
        await grain.GrantItemsAsync("k1", [Line(ItemContent.Shard, 10)]);
        _saveFaults.FailNext = 1;

        Func<Task> destroy = () => grain.DestroyItemAsync(0, 4);

        await destroy.Should().ThrowAsync<Exception>();
        (await grain.GetItemsAsync()).Single().Quantity.Should().Be(10);
        _events.OfType<ItemDestroyedEvent>().Should().BeEmpty();
        await DeactivateAsync(grain);
        (await RowsAsync()).Single().Quantity.Should().Be(10);
    }

    /// <summary>A part split off and destroyed before any flush is audited with its real id.</summary>
    [Fact]
    public async Task ADestroyedSplitPart_IsAuditedWithItsId()
    {
        PlayerCharacterGrain grain = await WarriorAsync();
        await grain.GrantItemsAsync("k1", [Line(ItemContent.Shard, 20)]);
        await grain.SplitItemAsync(0, 5, 1);

        await grain.DestroyItemAsync(1, 5);

        _events.OfType<ItemDestroyedEvent>().Single().ItemId.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ARemovalWhoseWriteFails_RemovesNothing()
    {
        PlayerCharacterGrain grain = await WarriorAsync();
        await grain.GrantItemsAsync("k1", [Line(ItemContent.Shard, 10)]);
        _saveFaults.FailNext = 1;

        Func<Task> remove = () => grain.RemoveItemsAsync(ItemContent.Shard.Id, 4);

        await remove.Should().ThrowAsync<Exception>();
        (await grain.GetItemsAsync()).Single().Quantity.Should().Be(10);
        _events.OfType<ItemRemovedEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task ADestroy_IsWrittenAtOnce()
    {
        PlayerCharacterGrain grain = await WarriorAsync();
        await grain.GrantItemsAsync("k1", [Line(ItemContent.Shard, 10)]);

        (await grain.DestroyItemAsync(0, 4)).Should().Be(ItemOutcome.Ok);

        (await RowsAsync()).Single().Quantity.Should().Be(6);
        _events
            .OfType<ItemDestroyedEvent>()
            .Should()
            .ContainSingle(e => e.Quantity == 4 && e.ItemId > 0);
    }

    [Fact]
    public async Task ARemoval_TakesFromStacksAndIsWrittenAtOnce()
    {
        PlayerCharacterGrain grain = await WarriorAsync();
        await grain.GrantItemsAsync("k1", [Line(ItemContent.Shard, 10)]);

        (await grain.RemoveItemsAsync(ItemContent.Shard.Id, 10)).Should().Be(ItemOutcome.Ok);

        (await RowsAsync()).Should().BeEmpty();
        _events.OfType<ItemRemovedEvent>().Should().ContainSingle(e => e.Quantity == 10);
    }

    private static ItemGrantLine Line(ItemDefinition definition, int quantity) =>
        new() { DefinitionId = definition.Id, Quantity = quantity };

    private async Task<PlayerCharacterGrain> WarriorAsync()
    {
        await StoreWarriorAsync();

        return await ActivateAsync();
    }

    private async Task StoreWarriorAsync()
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

    private async Task<PlayerCharacterGrain> ActivateAsync()
    {
        DbContextOptions<VortexDbContext> options = new DbContextOptionsBuilder<VortexDbContext>(
            _options
        )
            .AddInterceptors(_faults, _saveFaults)
            .Options;
        PlayerCharacterGrain grain =
            GrainActivationContext.CreateWithIntegerKey<PlayerCharacterGrain>(
                Player,
                new OptionsDbContextFactory(options),
                CharacterContent.Registry(_content, () => _contentVersion),
                new IStatSource[] { new ClassLevelStatSource(), new EquipmentStatSource() },
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
                SilentPresence.Grains(),
                Options.Create(new ItemConfig { BagSize = 2, MaxParcels = 1 })
            );
        await grain.OnActivateAsync(CancellationToken.None);

        return grain;
    }

    private static Task DeactivateAsync(PlayerCharacterGrain grain) =>
        grain.OnDeactivateAsync(
            new DeactivationReason(DeactivationReasonCode.ShuttingDown, "test"),
            CancellationToken.None
        );

    private async Task<List<RpgItemEntity>> RowsAsync()
    {
        await using VortexDbContext db = new(_options);

        return await db.RpgItems.AsNoTracking().Where(i => i.DeletedAt == null).ToListAsync();
    }
}
