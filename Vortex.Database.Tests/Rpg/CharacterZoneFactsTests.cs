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
using Vortex.Rpg.Characters.Configuration;
using Vortex.Rpg.Characters.Grains;
using Vortex.Rpg.Characters.Stats;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Stats;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Database.Tests.Rpg;

/// <summary>
/// Spec Z1 §4.3: what a zone knows of a character — its level and what it owns, bag and
/// equipment, not parcels — and that every change of either reaches the zone through the presence.
/// </summary>
public sealed class CharacterZoneFactsTests : IAsyncLifetime
{
    private const int Player = 7;

    private readonly List<CharacterZoneFacts> _forwarded = [];
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
    public async Task Facts_CountBagAndEquipment_NotParcels()
    {
        PlayerCharacterGrain grain = await WarriorAsync(
            (ItemContent.Shard, 2, ItemContainer.Bag, 0),
            (ItemContent.Sword, 1, ItemContainer.Equipment, (int)EquipmentSlot.Weapon),
            (ItemContent.Shard, 5, ItemContainer.Parcel, 0)
        );

        CharacterZoneFacts? facts = await grain.GetZoneFactsAsync();

        facts!.Level.Should().Be(1);
        facts
            .Items.Should()
            .BeEquivalentTo(
                new Dictionary<string, int>
                {
                    [ItemContent.Shard.Id] = 2,
                    [ItemContent.Sword.Id] = 1,
                }
            );
    }

    [Fact]
    public async Task WithoutACharacter_FactsAreNull()
    {
        PlayerCharacterGrain grain = Grain();
        await grain.OnActivateAsync(CancellationToken.None);

        (await grain.GetZoneFactsAsync()).Should().BeNull();
    }

    [Fact]
    public async Task AGrant_TellsTheZone()
    {
        PlayerCharacterGrain grain = await WarriorAsync();

        await grain.GrantItemsAsync(
            "k1",
            [new ItemGrantLine { DefinitionId = ItemContent.Shard.Id, Quantity = 3 }]
        );

        _forwarded.Should().ContainSingle().Which.Items[ItemContent.Shard.Id].Should().Be(3);
    }

    [Fact]
    public async Task ARemoval_TellsTheZone()
    {
        PlayerCharacterGrain grain = await WarriorAsync(
            (ItemContent.Shard, 3, ItemContainer.Bag, 0)
        );

        await grain.RemoveItemsAsync(ItemContent.Shard.Id, 1);

        _forwarded.Last().Items[ItemContent.Shard.Id].Should().Be(2);
    }

    [Fact]
    public async Task ALevelUp_TellsTheZone()
    {
        PlayerCharacterGrain grain = await WarriorAsync();

        await grain.GainXpAsync(100, "test");

        _forwarded.Should().ContainSingle().Which.Level.Should().Be(2);
    }

    [Fact]
    public async Task XpWithoutALevel_TellsNothing()
    {
        PlayerCharacterGrain grain = await WarriorAsync();

        await grain.GainXpAsync(10, "test");

        _forwarded.Should().BeEmpty();
    }

    /// <summary>A level-1 warrior holding the given items, already written; what the activation
    /// forwarded is forgotten.</summary>
    private async Task<PlayerCharacterGrain> WarriorAsync(
        params (ItemDefinition Definition, int Quantity, ItemContainer Container, int Slot)[] items
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

            foreach (
                (
                    ItemDefinition definition,
                    int quantity,
                    ItemContainer container,
                    int slot
                ) in items
            )
            {
                db.RpgItems.Add(
                    new RpgItemEntity
                    {
                        PlayerEntityId = Player,
                        DefinitionId = definition.Id,
                        Quantity = quantity,
                        Durability = definition.MaxDurability > 0 ? definition.MaxDurability : null,
                        Container = container,
                        Slot = slot,
                    }
                );
            }

            await db.SaveChangesAsync();
        }

        PlayerCharacterGrain grain = Grain();
        await grain.OnActivateAsync(CancellationToken.None);
        _forwarded.Clear();

        return grain;
    }

    private PlayerCharacterGrain Grain() =>
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

    private IGrainFactory Presence() =>
        FakeProxy.Create<IGrainFactory>(call =>
            call.Method.Name == nameof(IGrainFactory.GetGrain)
                ? FakeProxy.Create<IPlayerPresenceGrain>(inner =>
                {
                    if (
                        inner.Method.Name
                        == nameof(IPlayerPresenceGrain.OnCharacterFactsChangedAsync)
                    )
                    {
                        _forwarded.Add((CharacterZoneFacts)inner.Args![0]!);
                    }

                    return null;
                })
                : null
        );
}
