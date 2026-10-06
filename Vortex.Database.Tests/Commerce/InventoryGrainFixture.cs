using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Runtime;
using Vortex.Database.Context;
using Vortex.Furniture.Providers;
using Vortex.Inventory.Configuration;
using Vortex.Inventory.Fulfillment;
using Vortex.Inventory.Grains;
using Vortex.Primitives.Catalog.Providers;
using Vortex.Primitives.Events;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Furniture.Snapshots;
using Vortex.Primitives.Furniture.StuffData;
using Vortex.Primitives.Groups.Grains;
using Vortex.Primitives.Groups.Snapshots;
using Vortex.Primitives.Inventory.Factories;
using Vortex.Primitives.Inventory.Furniture;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Snapshots.Furniture;
using Vortex.Tests.Support;

namespace Vortex.Database.Tests.Commerce;

/// <summary>
/// Which step of a grant is armed to throw. The catalog suite's existing fake inventory grain is one
/// boolean — the grant throws or it does not — so every window <em>between</em> two of the grant's
/// commits is invisible to it: "the buyer was refunded" reads the same whether nothing was delivered
/// or four commits' worth of goods were.
/// </summary>
public enum CommerceFaultStep
{
    /// <summary>Nothing fails; the baseline every window is measured against.</summary>
    None,

    /// <summary>The pet's presence notification, raised after the pet row is committed.</summary>
    PetNotification,

    /// <summary>The bot's inventory composer, raised after the bot row is committed.</summary>
    BotNotification,

    /// <summary>
    /// The cross-grain effect grant — the last step of a catalog grant, and the one whose own comment
    /// says "a throw here propagates to the wallet's ExecutePurchaseAsync so the purchase
    /// auto-refunds".
    /// </summary>
    EffectGrant,

    /// <summary>
    /// The per-item notification that follows every committed furniture row. Combined with
    /// <see cref="InventoryGrainFixture.FailFurnitureNotificationAfter"/> it is how a grant is made to
    /// fail partway through a run of unit grants, which is the shape of the targeted-offer window.
    /// </summary>
    FurnitureNotification,
}

/// <summary>
/// A real <see cref="InventoryGrain"/> over a real (in-memory) database, with one named step armed to
/// throw. Every collaborator it reaches mid-grant is a recording fake, and each of those calls is a
/// place the grant can fail <em>after</em> having committed something.
/// </summary>
/// <remarks>
/// The EF in-memory provider rather than SQLite: the grant path is Add + SaveChanges throughout, so
/// nothing here needs ExecuteUpdate (which in-memory does not implement), while SQLite refuses to
/// insert any VortexEntity at all — created_at is identity-generated and updated_at computed, and
/// EnsureCreated gives neither a default.
/// </remarks>
internal sealed class InventoryGrainFixture : IDisposable
{
    public const int FLOOR_DEFINITION_ID = 55;

    public InventoryGrainFixture(int playerId)
    {
        PlayerId = playerId;
        DbOptions = new DbContextOptionsBuilder<VortexDbContext>()
            .UseInMemoryDatabase($"commerce-{Guid.NewGuid():N}")
            .Options;

        Grain = Build();
    }

    public int PlayerId { get; }

    public DbContextOptions<VortexDbContext> DbOptions { get; }

    public InventoryGrain Grain { get; }

    /// <summary>The step armed to throw. Set before the purchase, read during it.</summary>
    public CommerceFaultStep Fails { get; set; } = CommerceFaultStep.None;

    /// <summary>
    /// How many furniture notifications succeed before <see cref="CommerceFaultStep.FurnitureNotification"/>
    /// starts throwing. Zero means the first one throws.
    /// </summary>
    public int FailFurnitureNotificationAfter { get; set; }

    public List<(int EffectId, int SubType, int Duration)> EffectsGranted { get; } = [];

    private int _furnitureNotifications;

    public IDbContextFactory<VortexDbContext> DbContextFactory =>
        new TestDbContextFactory(DbOptions);

    // The durable record of what a grant actually committed, which is the only thing worth asserting
    // on: "the refund was called" is true in every one of these scenarios.
    public Task<int> FurnitureRowsAsync() =>
        CountAsync(db => db.Furnitures.CountAsync(f => f.PlayerEntityId == PlayerId));

    public Task<int> BadgeRowsAsync() =>
        CountAsync(db => db.PlayerBadges.CountAsync(b => b.PlayerEntityId == PlayerId));

    public Task<int> PetRowsAsync() =>
        CountAsync(db => db.Pets.CountAsync(p => p.OwnerPlayerEntityId == PlayerId));

    public Task<int> BotRowsAsync() =>
        CountAsync(db => db.Bots.CountAsync(b => b.OwnerPlayerEntityId == PlayerId));

    public void Dispose()
    {
        using VortexDbContext db = new(DbOptions);
        db.Database.EnsureDeleted();
    }

    private async Task<int> CountAsync(Func<VortexDbContext, Task<int>> count)
    {
        await using VortexDbContext db = new(DbOptions);

        return await count(db);
    }

    private void Fail(CommerceFaultStep step)
    {
        if (Fails == step)
        {
            throw new InvalidOperationException($"injected fault at {step}");
        }
    }

    private InventoryGrain Build()
    {
        InventoryGrain grain = new(
            DbContextFactory,
            Options.Create(new InventoryConfig()),
            BuildGrainFactory(),
            new StubDefinitionProvider(),
            // No parcels: every gift here is handed over unwrapped.
            FakeProxy.Create<IGiftWrappingProvider>(_ => null),
            new StubFurnitureLoader(),
            new StuffDataFactory(),
            new CatalogFulfillmentPlanner(
                new StubDefinitionProvider(),
                NullLogger<CatalogFulfillmentPlanner>.Instance
            ),
            FakeProxy.Create<IEventPublisher>(_ => Task.CompletedTask),
            NullLogger<InventoryGrain>.Instance
        );

        GrainContexts.Install(grain, "inventory", PlayerId);

        return grain;
    }

    private IGrainFactory BuildGrainFactory()
    {
        IPlayerPresenceGrain presence = FakeProxy.Create<IPlayerPresenceGrain>(call =>
        {
            switch (call.Method.Name)
            {
                case nameof(IPlayerPresenceGrain.OnFurnitureAddedAsync):
                    if (_furnitureNotifications++ >= FailFurnitureNotificationAfter)
                    {
                        Fail(CommerceFaultStep.FurnitureNotification);
                    }

                    break;
                case nameof(IPlayerPresenceGrain.OnPetAddedToInventoryAsync):
                    Fail(CommerceFaultStep.PetNotification);
                    break;
                case nameof(IPlayerPresenceGrain.SendComposerAsync):
                    Fail(CommerceFaultStep.BotNotification);
                    break;
                default:
                    break;
            }

            return Task.CompletedTask;
        });

        IPlayerEffectGrain effects = FakeProxy.Create<IPlayerEffectGrain>(call =>
        {
            if (call.Method.Name != nameof(IPlayerEffectGrain.AddEffectAsync))
            {
                return Task.CompletedTask;
            }

            Fail(CommerceFaultStep.EffectGrant);

            EffectsGranted.Add(((int)call.Args![0]!, (int)call.Args![1]!, (int)call.Args![2]!));

            return Task.CompletedTask;
        });

        return FakeProxy.Create<IGrainFactory>(call =>
        {
            if (!call.Method.IsGenericMethod)
            {
                return null;
            }

            Type grainType = call.Method.GetGenericArguments()[0];

            return grainType switch
            {
                Type t when t == typeof(IPlayerPresenceGrain) => presence,
                Type t when t == typeof(IPlayerEffectGrain) => effects,
                Type t when t == typeof(IGroupDirectoryGrain) =>
                    FakeProxy.Create<IGroupDirectoryGrain>(_ =>
                        Task.FromResult<GuildFurniIdentitySnapshot?>(null)
                    ),
                _ => null,
            };
        });
    }

    private sealed class TestDbContextFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);
    }

    private sealed class StubDefinitionProvider : IFurnitureDefinitionProvider
    {
        public FurnitureDefinitionSnapshot? TryGetDefinition(int id) => Definition(id);

        public FurnitureDefinitionSnapshot? TryGetDefinitionByName(string name) => null;

        public Task ReloadAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class StubFurnitureLoader : IInventoryFurnitureLoader
    {
        public Task<IReadOnlyList<IFurnitureItem>> LoadByPlayerIdAsync(
            PlayerId playerId,
            CancellationToken ct
        ) => Task.FromResult<IReadOnlyList<IFurnitureItem>>([]);

        public IFurnitureItem CreateFromRoomItemSnapshot(RoomItemSnapshot snapshot) =>
            throw new NotSupportedException();
    }

    public static FurnitureDefinitionSnapshot Definition(int id) =>
        new()
        {
            Id = id,
            SpriteId = 1,
            Name = "fault_test_chair",
            ProductType = ProductType.Floor,
            FurniCategory = FurnitureCategory.Default,
            LogicName = "default_floor",
            TotalStates = 1,
            Width = 1,
            Length = 1,
            StackHeight = Altitude.FromInt(100),
            CanStack = true,
            CanWalk = false,
            CanSit = false,
            CanLay = false,
            CanRecycle = false,
            CanTrade = true,
            CanGroup = false,
            CanSell = true,
            UsagePolicy = FurnitureUsageType.Nobody,
            ExtraData = null,
            StuffDataType = StuffDataType.LegacyKey,
        };
}

/// <summary>
/// A grain reads <c>this.GetPrimaryKeyLong()</c>, which resolves through the activation context
/// Orleans would normally install. Four suites had their own copy of this reflection before.
/// </summary>
internal static class GrainContexts
{
    public static void Install(Grain grain, string grainType, int key)
    {
        GrainId grainId = GrainId.Create(
            GrainType.Create(grainType),
            GrainIdKeyExtensions.CreateIntegerKey(key)
        );

        IGrainContext context = FakeProxy.Create<IGrainContext>(call =>
            call.Method.Name == $"get_{nameof(IGrainContext.GrainId)}" ? grainId : null
        );

        System.Reflection.FieldInfo field =
            typeof(Grain).GetField(
                "<GrainContext>k__BackingField",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
            ) ?? throw new InvalidOperationException("Grain.GrainContext backing field moved.");

        field.SetValue(grain, context);
    }
}
