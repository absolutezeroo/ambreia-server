using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Object.Logic.Avatars;
using Vortex.Rooms.Object.Avatars.Player;
using Vortex.Rooms.Tests.Support;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Avatars;

/// <summary>
/// The tile stack lost the avatar first and the two lookup tables only after the detach hook and
/// the broadcast: a hook that threw left a ghost that still counted as present, still ticked, and
/// was never announced as gone, while its tile no longer blocked anyone.
/// </summary>
public sealed class AvatarRemovalTests
{
    [Fact]
    public async Task ADetachHookThatThrows_StillTakesTheAvatarOutOfTheRoom()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);
        PlayerId player = new(7);

        RoomPlayerAvatar avatar = harness.PutRealPlayerOnTile(player, 3, 3);
        avatar.SetLogic(
            FakeProxy.Create<IRoomPlayerLogic>(call =>
                call.Method.Name == nameof(IRoomPlayerLogic.OnDetachAsync)
                    ? Task.FromException(new InvalidOperationException("hook failed"))
                    : null
            )
        );

        await harness.Grain.AvatarModule.RemoveAvatarFromPlayerAsync(
            harness.ContextFor(player),
            player,
            CancellationToken.None
        );

        harness.Grain._state.AvatarsByPlayerId.Should().NotContainKey(player);
        harness.Grain._state.AvatarsByObjectId.Should().NotContainKey(avatar.ObjectId);
        harness
            .Grain._state.TileAvatarStacks[harness.Grain.MapModule.ToIdx(3, 3)]
            .Should()
            .NotContain(avatar.ObjectId);
    }

    /// <summary>
    /// A spawn is a first step onto the door tile. An avatar not yet placed reads as (0, 0), so a
    /// door on (0, 0) looked like a step onto the tile already stood on and was skipped: the avatar
    /// never entered the tile's stack and never got its height.
    /// </summary>
    [Fact]
    public async Task ASpawnOnADoorAtTheOrigin_PutsTheAvatarOnItsTile()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);
        RoomPlayerAvatar avatar = harness.PutRealPlayerInRoom(new PlayerId(8), 0, 0);
        avatar.NextTileId = harness.Grain.MapModule.ToIdx(0, 0);

        await harness.Grain.AvatarModule.ProcessNextAvatarStepAsync(avatar, CancellationToken.None);

        harness.Grain._state.TileAvatarStacks[0].Should().Contain(avatar.ObjectId);
    }
}
