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
    private const int HorseType = 15;

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
            db.Furnitures.Add(
                new FurnitureEntity
                {
                    Id = SaddleItemId,
                    PlayerEntityId = RoomHarness.Owner.Value,
                    RoomEntityId = RoomHarness.RoomIdValue,
                }
            );

            await db.SaveChangesAsync().ConfigureAwait(true);
        }

        await harness
            .Grain.PetSystem.EnsurePetsLoadedAsync(CancellationToken.None)
            .ConfigureAwait(true);

        harness.Grain._state.ItemsById[new RoomObjectId(SaddleItemId)] = Saddle();

        return harness;
    }

    /// <summary>
    /// A saddle the room holds but has not put on its map: consuming it then stops short of the
    /// logic hooks, which keeps this test to the saddle itself.
    /// </summary>
    private static IRoomItem Saddle()
    {
        FurnitureDefinitionSnapshot definition = new()
        {
            Id = 77,
            SpriteId = 77,
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
                $"get_{nameof(IRoomItem.ObjectId)}" => new RoomObjectId(SaddleItemId),
                $"get_{nameof(IRoomItem.OwnerId)}" => RoomHarness.Owner,
                $"get_{nameof(IRoomItem.Definition)}" => definition,
                _ => null,
            }
        );
    }
}
