using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Database.Entities.Furniture;
using Vortex.Database.Entities.Pets;
using Vortex.Primitives.Events;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Furniture.Snapshots;
using Vortex.Primitives.Pets.Snapshots;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Rooms.Grains.Systems;
using Vortex.Rooms.Tests.Support;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Pets;

/// <summary>
/// A saddle is a furniture the owner uses on a pet (header 2099, <c>CustomizePetWithFurni</c>).
/// The client offers it from <c>FurniturePetCustomizationLogic</c> and tells it apart by the
/// furniture's category, <c>PET_SADDLE</c> (16) -- the same field the server reads. Nothing used
/// to set <c>HasSaddle</c>, so no pet could ever be ridden.
/// </summary>
public sealed class PetSaddleTests
{
    private const int PetId = 900;
    private const int SaddleItemId = 4321;
    private const int SecondSaddleItemId = 4322;
    private const int HorseType = 15;
    private const int SaddleDefinitionId = 77;

    [Fact]
    public async Task TheOwnerSaddlesTheirPet_AndTheSaddleIsSpent()
    {
        RoomHarness harness = await ArrangeAsync().ConfigureAwait(true);

        await harness
            .Grain.ApplySaddleToPetAsync(
                harness.ContextFor(RoomHarness.Owner),
                PetId,
                new RoomObjectId(SaddleItemId),
                CancellationToken.None
            )
            .ConfigureAwait(true);

        harness.Grain._state.PetsById[PetId].HasSaddle.Should().BeTrue();

        await using VortexDbContext db = harness.NewDbContext();
        PetEntity row = db.Pets.Single(p => p.Id == PetId);
        row.HasSaddle.Should().BeTrue("the saddle is written to the pet's row");
        RoomPetRuntime
            .ToSnapshot(row)
            .SaddleDefinitionId.Should()
            .Be(SaddleDefinitionId, "a reloaded room still knows which saddle to hand back");
        RoomPetRuntime
            .ToSnapshot(row)
            .HasSaddle.Should()
            .BeTrue("and read back from it, or a room reload would take it off again");
        db.Furnitures.IgnoreQueryFilters()
            .Single(f => f.Id == SaddleItemId)
            .DeletedAt.Should()
            .NotBeNull();
        harness
            .PublishedEvents.OfType<ItemDeletedEvent>()
            .Should()
            .ContainSingle(e => e.ItemId == SaddleItemId);
    }

    [Fact]
    public async Task SomebodyElseCannotSaddleThePet()
    {
        RoomHarness harness = await ArrangeAsync().ConfigureAwait(true);

        await harness
            .Grain.ApplySaddleToPetAsync(
                harness.ContextFor(RoomHarness.Stranger),
                PetId,
                new RoomObjectId(SaddleItemId),
                CancellationToken.None
            )
            .ConfigureAwait(true);

        harness.Grain._state.PetsById[PetId].HasSaddle.Should().BeFalse();

        await using VortexDbContext db = harness.NewDbContext();
        db.Furnitures.Single(f => f.Id == SaddleItemId).DeletedAt.Should().BeNull();
        harness.PublishedEvents.OfType<ItemDeletedEvent>().Should().BeEmpty();
    }

    /// <summary>
    /// Two saddles for one pet, the second arriving while the first is still being spent -- held
    /// open here on the deletion event, the last await before the pet wears it. The second is
    /// refused before its own furniture is touched; otherwise it is spent for a saddle the pet
    /// already has.
    /// </summary>
    [Fact]
    public async Task ASecondSaddleArrivingMidway_IsRefusedAndNotSpent()
    {
        RoomHarness harness = await ArrangeAsync().ConfigureAwait(true);
        TaskCompletionSource firstPublished = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        harness.PublishResult = firstPublished.Task;

        Task first = harness.Grain.ApplySaddleToPetAsync(
            harness.ContextFor(RoomHarness.Owner),
            PetId,
            new RoomObjectId(SaddleItemId),
            CancellationToken.None
        );

        first.IsCompleted.Should().BeFalse("the first saddle is held on its deletion event");

        harness.PublishResult = null;

        await harness
            .Grain.ApplySaddleToPetAsync(
                harness.ContextFor(RoomHarness.Owner),
                PetId,
                new RoomObjectId(SecondSaddleItemId),
                CancellationToken.None
            )
            .ConfigureAwait(true);

        firstPublished.SetResult();
        await first.ConfigureAwait(true);

        harness.Grain._state.PetsById[PetId].HasSaddle.Should().BeTrue();

        await using VortexDbContext db = harness.NewDbContext();
        db.Furnitures.IgnoreQueryFilters()
            .Single(f => f.Id == SecondSaddleItemId)
            .DeletedAt.Should()
            .BeNull("the pet already had a saddle on its way");
        harness
            .PublishedEvents.OfType<ItemDeletedEvent>()
            .Should()
            .ContainSingle(e => e.ItemId == SaddleItemId);
    }

    /// <summary>
    /// Fitting spends the saddle furniture, so taking it off has to hand one back: the owner
    /// otherwise loses a saddle every time they unsaddle their pet.
    /// </summary>
    [Fact]
    public async Task TheOwnerRemovesTheSaddle_AndGetsOneSaddleBack()
    {
        RoomHarness harness = await ArrangeSaddledAsync().ConfigureAwait(true);

        await harness
            .Grain.RemoveSaddleFromPetAsync(
                harness.ContextFor(RoomHarness.Owner),
                PetId,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        harness.Grain._state.PetsById[PetId].HasSaddle.Should().BeFalse();
        harness
            .FurnitureGranted.Should()
            .Equal([(RoomHarness.Owner, SaddleDefinitionId)], "the saddle goes back to its owner");
    }

    [Fact]
    public async Task RemovingTheSaddleTwice_ReturnsOnlyOneSaddle()
    {
        RoomHarness harness = await ArrangeSaddledAsync().ConfigureAwait(true);

        for (int i = 0; i < 2; i++)
        {
            await harness
                .Grain.RemoveSaddleFromPetAsync(
                    harness.ContextFor(RoomHarness.Owner),
                    PetId,
                    CancellationToken.None
                )
                .ConfigureAwait(true);
        }

        harness.FurnitureGranted.Should().ContainSingle();
    }

    [Fact]
    public async Task SomebodyElseCannotRemoveTheSaddle()
    {
        RoomHarness harness = await ArrangeSaddledAsync().ConfigureAwait(true);

        await harness
            .Grain.RemoveSaddleFromPetAsync(
                harness.ContextFor(RoomHarness.Stranger),
                PetId,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        harness.Grain._state.PetsById[PetId].HasSaddle.Should().BeTrue();
        harness.FurnitureGranted.Should().BeEmpty();
    }

    private static async Task<RoomHarness> ArrangeSaddledAsync()
    {
        RoomHarness harness = await ArrangeAsync().ConfigureAwait(true);

        await harness
            .Grain.ApplySaddleToPetAsync(
                harness.ContextFor(RoomHarness.Owner),
                PetId,
                new RoomObjectId(SaddleItemId),
                CancellationToken.None
            )
            .ConfigureAwait(true);

        return harness;
    }

    private static async Task<RoomHarness> ArrangeAsync()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        await using (VortexDbContext db = harness.NewDbContext())
        {
            db.Pets.Add(
                new PetEntity
                {
                    Id = PetId,
                    OwnerPlayerEntityId = RoomHarness.Owner.Value,
                    OwnerPlayerEntity = null!,
                    RoomEntityId = RoomHarness.RoomIdValue,
                    Name = "Comet",
                    Type = HorseType,
                    Race = 0,
                    Color = "FFFFFF",
                    Gender = AvatarGenderType.Male,
                    Level = 1,
                    Experience = 0,
                    Energy = 100,
                    Nutrition = 100,
                    Respect = 0,
                    X = 2,
                    Y = 2,
                    Z = 0,
                    Direction = 2,
                }
            );
            foreach (int itemId in new[] { SaddleItemId, SecondSaddleItemId })
            {
                db.Furnitures.Add(
                    new FurnitureEntity
                    {
                        Id = itemId,
                        PlayerEntityId = RoomHarness.Owner.Value,
                        RoomEntityId = RoomHarness.RoomIdValue,
                    }
                );
            }

            await db.SaveChangesAsync().ConfigureAwait(true);
        }

        await harness
            .Grain.PetSystem.EnsurePetsLoadedAsync(CancellationToken.None)
            .ConfigureAwait(true);

        harness.Grain._state.ItemsById[new RoomObjectId(SaddleItemId)] = Saddle(SaddleItemId);
        harness.Grain._state.ItemsById[new RoomObjectId(SecondSaddleItemId)] = Saddle(
            SecondSaddleItemId
        );

        return harness;
    }

    /// <summary>
    /// A saddle the room holds but has not put on its map: consuming it then stops short of the
    /// logic hooks, which keeps this test to the saddle itself.
    /// </summary>
    private static IRoomItem Saddle(int itemId)
    {
        FurnitureDefinitionSnapshot definition = new()
        {
            Id = SaddleDefinitionId,
            SpriteId = SaddleDefinitionId,
            Name = "horse_saddle1",
            ProductType = ProductType.Floor,
            FurniCategory = FurnitureCategory.PetSaddle,
            LogicName = "default",
            TotalStates = 1,
            Width = 1,
            Length = 1,
            StackHeight = default,
            CanStack = false,
            CanWalk = false,
            CanSit = false,
            CanLay = false,
            CanRecycle = false,
            CanTrade = true,
            CanGroup = false,
            CanSell = true,
            UsagePolicy = FurnitureUsageType.Nobody,
            ExtraData = null,
            StuffDataType = default,
        };

        return FakeProxy.Create<IRoomItem>(call =>
            call.Method.Name switch
            {
                $"get_{nameof(IRoomItem.ObjectId)}" => new RoomObjectId(itemId),
                $"get_{nameof(IRoomItem.OwnerId)}" => RoomHarness.Owner,
                $"get_{nameof(IRoomItem.Definition)}" => definition,
                _ => null,
            }
        );
    }
}
