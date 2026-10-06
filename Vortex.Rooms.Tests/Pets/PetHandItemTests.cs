using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Database.Context;
using Vortex.Database.Entities.Room;
using Vortex.Primitives.Action;
using Vortex.Primitives.Pets.Snapshots;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Rooms.Object.Avatars.Player;
using Vortex.Rooms.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Pets;

/// <summary>
/// Giving a pet what you are holding. The point of the seeded table is that only some hand items
/// are treats: a pet takes the water and leaves the camera, and an item it will not take stays in
/// the hand rather than vanishing. Pets have no needs since M0, so a treat changes no stat.
/// </summary>
public sealed class PetHandItemTests
{
    private const int PetId = 3;
    private const int Water = 7;
    private const int Camera = 20;

    private static readonly PlayerId Feeder = new(101);

    [Fact]
    public async Task GivingAPetADrink_EmptiesTheHandAndChangesNoStat()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        await SeedHandItemAsync(harness, Water, "Water", nutrition: 0, thirst: 15)
            .ConfigureAwait(true);

        RoomPlayerAvatar feeder = harness.PutRealPlayerInRoom(Feeder, 2, 2);
        await harness.PutPetInRoomAsync(PetId, 2, 3).ConfigureAwait(true);

        harness.Grain.HandItemModule.Give(Feeder, Water);

        bool fed = await harness
            .Grain.PetSystem.ConsumeHandItemAsync(
                harness.ContextFor(Feeder),
                PetId,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        fed.Should().BeTrue();
        harness.Grain._state.PetsById[PetId].Thirst.Should().Be(100);
        feeder.CarryItemId.Should().Be(0, "the pet drank it");
    }

    [Fact]
    public async Task GivingAPetSomethingItWillNotTake_LeavesItInTheHand()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        // No row for a camera, which is the table's way of saying a pet has no use for one.
        RoomPlayerAvatar feeder = harness.PutRealPlayerInRoom(Feeder, 2, 2);
        await harness.PutPetInRoomAsync(PetId, 2, 3).ConfigureAwait(true);

        harness.Grain.HandItemModule.Give(Feeder, Camera);

        bool fed = await harness
            .Grain.PetSystem.ConsumeHandItemAsync(
                harness.ContextFor(Feeder),
                PetId,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        fed.Should().BeFalse();
        feeder
            .CarryItemId.Should()
            .Be(Camera, "swallowing it silently would be worse than refusing");
    }

    [Fact]
    public async Task GivingAPetAcrossTheRoom_IsRefused()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        await SeedHandItemAsync(harness, Water, "Water", nutrition: 0, thirst: 15)
            .ConfigureAwait(true);

        RoomPlayerAvatar feeder = harness.PutRealPlayerInRoom(Feeder, 2, 2);
        await harness.PutPetInRoomAsync(PetId, 9, 9).ConfigureAwait(true);

        harness.Grain.HandItemModule.Give(Feeder, Water);

        bool fed = await harness
            .Grain.PetSystem.ConsumeHandItemAsync(
                harness.ContextFor(Feeder),
                PetId,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        fed.Should().BeFalse();
        feeder.CarryItemId.Should().Be(Water);
    }

    [Fact]
    public async Task GivingWithAnEmptyHand_IsRefused()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        harness.PutRealPlayerInRoom(Feeder, 2, 2);
        await harness.PutPetInRoomAsync(PetId, 2, 3).ConfigureAwait(true);

        bool fed = await harness
            .Grain.PetSystem.ConsumeHandItemAsync(
                harness.ContextFor(Feeder),
                PetId,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        fed.Should().BeFalse();
    }

    private static async Task SeedHandItemAsync(
        RoomHarness harness,
        int handItemId,
        string name,
        int nutrition,
        int thirst
    )
    {
        await using VortexDbContext dbCtx = harness.NewDbContext();

        dbCtx.HandItems.Add(
            new HandItemEntity
            {
                HandItemId = handItemId,
                Name = name,
                Nutrition = nutrition,
                Thirst = thirst,
            }
        );

        await dbCtx.SaveChangesAsync().ConfigureAwait(true);
    }
}
