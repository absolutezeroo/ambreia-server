using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Players;
using Vortex.Rooms.Object.Avatars.Player;
using Vortex.Rooms.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Avatars;

/// <summary>Spec M1b §7.2: a slowed avatar skips steps — at 40 % it takes two and a half times as
/// long — and a stunned one does not walk until freed.</summary>
public sealed class SlowedWalkTests
{
    private RoomHarness _harness = null!;

    private async Task<RoomPlayerAvatar> WalkerAsync(int speedPercent)
    {
        _harness = await RoomHarness.CreateAsync().ConfigureAwait(true);
        RoomPlayerAvatar avatar = _harness.PutRealPlayerOnTile(new PlayerId(7), 1, 1);
        _harness.Grain.MapModule.AddAvatar(avatar, false);
        avatar.MoveSpeedPercent = speedPercent;
        (
            await _harness
                .Grain.AvatarModule.WalkAvatarToAsync(avatar, 6, 1, CancellationToken.None)
                .ConfigureAwait(true)
        )
            .Should()
            .BeTrue();

        return avatar;
    }

    private async Task<int> TicksToArriveAsync(RoomPlayerAvatar avatar)
    {
        for (int ticks = 1; ticks <= 40; ticks++)
        {
            await _harness.TickAvatarsAsync().ConfigureAwait(true);

            if (avatar.X == 6 && !avatar.IsWalking)
            {
                return ticks;
            }
        }

        return int.MaxValue;
    }

    [Fact]
    public async Task AtFullSpeed_FiveTilesTakeAboutFiveTicks()
    {
        RoomPlayerAvatar avatar = await WalkerAsync(100);

        (await TicksToArriveAsync(avatar)).Should().BeInRange(5, 7);
    }

    [Fact]
    public async Task AtFortyPercent_TheWalkTakesTwoAndAHalfTimesAsLong()
    {
        RoomPlayerAvatar fast = await WalkerAsync(100);
        int full = await TicksToArriveAsync(fast);
        RoomPlayerAvatar slow = await WalkerAsync(40);

        int slowed = await TicksToArriveAsync(slow);

        slowed.Should().BeInRange((int)(full * 2.2), (int)(full * 2.8) + 1);
    }

    [Fact]
    public async Task ARootedAvatar_StartsNoWalk_UntilFreed()
    {
        _harness = await RoomHarness.CreateAsync().ConfigureAwait(true);
        RoomPlayerAvatar avatar = _harness.PutRealPlayerOnTile(new PlayerId(7), 1, 1);
        _harness.Grain.MapModule.AddAvatar(avatar, false);

        _harness.Grain.CombatRuntime.SetRooted(avatar.ObjectId.Value, true);

        (
            await _harness
                .Grain.AvatarModule.WalkAvatarToAsync(avatar, 6, 1, CancellationToken.None)
                .ConfigureAwait(true)
        )
            .Should()
            .BeFalse();

        _harness.Grain.CombatRuntime.SetRooted(avatar.ObjectId.Value, false);

        (
            await _harness
                .Grain.AvatarModule.WalkAvatarToAsync(avatar, 6, 1, CancellationToken.None)
                .ConfigureAwait(true)
        )
            .Should()
            .BeTrue();
    }
}
