using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Players;
using Vortex.Rooms.Object.Avatars.Player;
using Vortex.Rooms.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Avatars;

/// <summary>
/// A player whose socket dropped stays in the room, still, for the grace period (spec Personnage
/// §10): the walk under way stops on the tile reached, and the avatar stays there.
/// </summary>
public sealed class AvatarStopTests
{
    [Fact]
    public async Task AStoppedAvatar_StaysOnItsTile()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);
        PlayerId player = new(7);
        RoomPlayerAvatar avatar = harness.PutRealPlayerOnTile(player, 1, 1);
        harness.Grain.MapModule.AddAvatar(avatar, false);
        await harness
            .Grain.AvatarModule.WalkAvatarToAsync(avatar, 5, 1, CancellationToken.None)
            .ConfigureAwait(true);
        await harness.TickAvatarsAsync(2).ConfigureAwait(true);

        (
            await harness.Grain.StopAvatarAsync(
                harness.ContextFor(player),
                player,
                CancellationToken.None
            )
        )
            .Should()
            .BeTrue();
        (int x, int y) = (avatar.X, avatar.Y);
        await harness.TickAvatarsAsync(10).ConfigureAwait(true);

        avatar.IsWalking.Should().BeFalse();
        (avatar.X, avatar.Y).Should().Be((x, y));
        harness.Grain._state.AvatarsByPlayerId.Should().ContainKey(player);
    }

    [Fact]
    public async Task StoppingAPlayerWhoIsNotThere_IsFalse()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        (
            await harness.Grain.StopAvatarAsync(
                harness.ContextFor(new PlayerId(9)),
                new PlayerId(9),
                CancellationToken.None
            )
        )
            .Should()
            .BeFalse();
    }
}
