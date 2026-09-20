using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Pets.Snapshots;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;
using Vortex.Rooms.Grains.Systems;
using Vortex.Rooms.Object.Avatars.Player;
using Vortex.Rooms.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Pets;

/// <summary>
/// Scratching a pet is not done at a distance: the hotel walks you up to it and the scratch lands
/// when you arrive.
///
/// Nothing on the server did that, and the client filled the gap by accident — the click on the pet
/// fell through to the floor plane behind it and sent a walk to whatever tile the cursor happened to
/// be over, which is the tile the pet is standing on. You ended up drawn on top of the pet.
/// </summary>
public sealed class PetScratchApproachTests
{
    private const int PetId = 77;
    private const int PetX = 3;
    private const int PetY = 3;

    [Fact]
    public async Task ScratchingFromAcrossTheRoom_WalksUpToThePetFirst()
    {
        (RoomHarness harness, RoomPlayerAvatar scratcher) = await RoomWithAPetAsync(0, 0)
            .ConfigureAwait(true);

        await harness
            .Grain.PetSystem.RespectPetAsync(
                harness.ContextFor(RoomHarness.Stranger),
                PetId,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        scratcher.IsWalking.Should().BeTrue("the scratcher has to get there first");
        Pet(harness).Respect.Should().Be(0, "and the scratch has not landed yet");

        await harness.TickAvatarsAsync(30).ConfigureAwait(true);

        scratcher.IsWalking.Should().BeFalse();
        Distance(scratcher).Should().Be(1, "you stop next to the pet, never on it");
        Pet(harness).Respect.Should().Be(1, "and that is when the scratch lands");
    }

    /// <summary>Already within reach: no walk, and the scratch is immediate.</summary>
    [Fact]
    public async Task ScratchingFromTheNextTile_DoesNotMoveAnybody()
    {
        (RoomHarness harness, RoomPlayerAvatar scratcher) = await RoomWithAPetAsync(
                PetX + 1,
                PetY + 1
            )
            .ConfigureAwait(true);

        await harness
            .Grain.PetSystem.RespectPetAsync(
                harness.ContextFor(RoomHarness.Stranger),
                PetId,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        scratcher.IsWalking.Should().BeFalse();
        (scratcher.X, scratcher.Y).Should().Be((PetX + 1, PetY + 1));
        Pet(harness).Respect.Should().Be(1);

        // The pet answers, which is where the capture ends. Its mood line is content and this room
        // has no pet_vocals rows, so only the gesture is on the wire here.
        harness
            .BroadcastToRoom.OfType<UserUpdateMessageComposer>()
            .SelectMany(composer => composer.Avatars)
            .Should()
            .Contain(avatar => avatar.Status == PetAnimation.JoyStatus);
    }

    private static async Task<(RoomHarness Harness, RoomPlayerAvatar Scratcher)> RoomWithAPetAsync(
        int scratcherX,
        int scratcherY
    )
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        await harness.PutPetInRoomAsync(PetId, PetX, PetY).ConfigureAwait(true);

        return (harness, harness.PutRealPlayerInRoom(RoomHarness.Stranger, scratcherX, scratcherY));
    }

    private static PetSnapshot Pet(RoomHarness harness) => harness.Grain._state.PetsById[PetId];

    private static int Distance(RoomPlayerAvatar scratcher) =>
        Math.Max(Math.Abs(scratcher.X - PetX), Math.Abs(scratcher.Y - PetY));
}
