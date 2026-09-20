using FluentAssertions;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Rooms.Object.Avatars.Player;
using Xunit;

namespace Vortex.Rooms.Tests.Avatars;

/// <summary>
/// A dance and a sit/lay posture cannot coexist on Habbo: taking a seat ends the dance. The avatar
/// used to keep its dance type while sitting, so the posture change drops the dance on the avatar
/// itself -- every caller (the posture packet, walking onto a chair) goes through
/// <c>Sit</c>/<c>Lay</c>.
/// </summary>
/// <remarks>
/// <c>StandingBackUp</c> asserted the opposite until 2026-09-20, on no stated grounds. The summary
/// above sources one fact — a dance and a posture cannot coexist — and dropping the dance for good
/// was one way to satisfy it, not a second fact about Habbo. Interrupting it and putting it back
/// is the other way, it is what the hotel wants, and it leaves the sourced rule untouched: the
/// dance is still gone for as long as the avatar is seated, and <c>SetDance</c> still refuses
/// while a posture is on.
/// </remarks>
public sealed class RoomPlayerAvatarPostureTests
{
    private static RoomPlayerAvatar CreateAvatar() =>
        new() { ObjectId = new RoomObjectId(1), PlayerId = new PlayerId(42) };

    [Fact]
    public void Sitting_CancelsTheDance()
    {
        RoomPlayerAvatar avatar = CreateAvatar();
        avatar.SetDance(AvatarDanceType.PogoMogo).Should().BeTrue();

        avatar.Sit(true);

        avatar.DanceType.Should().Be(AvatarDanceType.None);
        avatar.HasStatus(AvatarStatusType.Sit).Should().BeTrue();
    }

    [Fact]
    public void Laying_CancelsTheDance()
    {
        RoomPlayerAvatar avatar = CreateAvatar();
        avatar.SetDance(AvatarDanceType.TheRollie).Should().BeTrue();

        avatar.Lay(true);

        avatar.DanceType.Should().Be(AvatarDanceType.None);
        avatar.HasStatus(AvatarStatusType.Lay).Should().BeTrue();
    }

    [Fact]
    public void StandingBackUp_BringsTheInterruptedDanceBack()
    {
        RoomPlayerAvatar avatar = CreateAvatar();
        avatar.SetDance(AvatarDanceType.Dance);
        avatar.Sit(true);

        avatar.Sit(false);

        avatar.HasStatus(AvatarStatusType.Sit).Should().BeFalse();
        avatar.DanceType.Should().Be(AvatarDanceType.Dance);
    }

    /// <summary>Standing up resumes a dance; it does not invent one.</summary>
    [Fact]
    public void StandingBackUp_AfterSittingWithoutDancing_StaysStill()
    {
        RoomPlayerAvatar avatar = CreateAvatar();
        avatar.Sit(true);

        avatar.Sit(false);

        avatar.DanceType.Should().Be(AvatarDanceType.None);
    }

    /// <summary>
    /// Sitting, standing, sitting again: the second posture must not put back a dance the player
    /// has already been given back once and did not restart.
    /// </summary>
    [Fact]
    public void TheResumedDance_IsNotHandedBackASecondTime()
    {
        RoomPlayerAvatar avatar = CreateAvatar();
        avatar.SetDance(AvatarDanceType.Dance);
        avatar.Sit(true);
        avatar.Sit(false);
        avatar.SetDance(AvatarDanceType.None);

        avatar.Sit(true);
        avatar.Sit(false);

        avatar.DanceType.Should().Be(AvatarDanceType.None);
    }

    /// <summary>
    /// Walking from a chair onto a bed keeps the dance remembered across both postures — the
    /// second <c>Stash</c> must not overwrite it with the None the first one left behind.
    /// </summary>
    [Fact]
    public void MovingFromASeatToABed_StillResumesTheOriginalDance()
    {
        RoomPlayerAvatar avatar = CreateAvatar();
        avatar.SetDance(AvatarDanceType.TheRollie);
        avatar.Sit(true);

        avatar.Lay(true);
        avatar.Sit(false);
        avatar.Lay(false);

        avatar.DanceType.Should().Be(AvatarDanceType.TheRollie);
    }

    [Fact]
    public void DancingWhileSeated_IsStillRefused()
    {
        RoomPlayerAvatar avatar = CreateAvatar();
        avatar.Sit(true);

        avatar.SetDance(AvatarDanceType.Dance).Should().BeFalse();
        avatar.DanceType.Should().Be(AvatarDanceType.None);
    }
}
