using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Database.Context;
using Vortex.Database.Entities.Room;
using Vortex.Primitives.Events;
using Vortex.Primitives.Navigator.Enums;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Rooms.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Permissions;

/// <summary>
/// The two staff powers no in-room controller level can gate — the room tool's checkboxes and the
/// navigator's staff pick. Both are members of public grain interfaces, and both used to be
/// authorized by their packet handler alone: a handler is a convenience, not a security boundary,
/// because anything in the cluster that can name the room can call the grain directly
/// (ROOMG-GATE-038).
/// </summary>
public sealed class StaffPowerGrainGateTests
{
    [Fact]
    public async Task TheRoomTool_RefusesAnActorWithoutModerateAny()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        await ApplyRoomToolAsync(harness).ConfigureAwait(true);

        // The room tool announces itself whenever it runs, whether or not a checkbox changed
        // anything, so this is what says the call reached the work rather than the gate.
        harness.PublishedEvents.OfType<RoomModeratedByStaffEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task TheRoomTool_RunsForAnActorWithModerateAny()
    {
        RoomHarness harness = await RoomHarness
            .CreateAsync(capabilities: [Capabilities.Room.ModerateAny])
            .ConfigureAwait(true);

        await ApplyRoomToolAsync(harness).ConfigureAwait(true);

        harness.PublishedEvents.OfType<RoomModeratedByStaffEvent>().Should().ContainSingle();
    }

    private static Task<bool> ApplyRoomToolAsync(RoomHarness harness) =>
        harness.Grain.ApplyStaffRoomActionsAsync(
            RoomHarness.Stranger,
            unlockDoor: true,
            resetNameAndDescription: false,
            kickUsers: false,
            CancellationToken.None
        );

    [Fact]
    public async Task TheStaffPick_IsNotAppliedWithoutTheCapability()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        bool? result = await harness
            .Grain.ToggleStaffPickAsync(RoomHarness.Stranger, CancellationToken.None)
            .ConfigureAwait(true);

        result.Should().BeNull("a refusal is not a state the caller should echo back");
        harness.Grain._state.RoomSnapshot.StaffPick.Should().BeFalse();
    }

    /// <summary>
    /// The pick has to come from the stored row, because the client never asks for a state.
    /// </summary>
    /// <remarks>
    /// <c>RoomInfoViewCtrl.onStaffPick</c> flips only its own button caption and then posts back
    /// the value it already believed, so a server that obeyed that boolean wrote the value already
    /// stored — clicking "pick" did nothing at all, and the public rooms view stayed empty for
    /// every hotel. The reference emulator reads the room id and nothing else for the same reason.
    /// </remarks>
    [Fact]
    public async Task TheStaffPick_FlipsTheStoredValueRatherThanObeyingTheCaller()
    {
        RoomHarness harness = await RoomHarness
            .CreateAsync(capabilities: [Capabilities.Navigator.StaffPick])
            .ConfigureAwait(true);

        await SeedRoomRowAsync(harness).ConfigureAwait(true);

        bool? picked = await harness
            .Grain.ToggleStaffPickAsync(RoomHarness.Stranger, CancellationToken.None)
            .ConfigureAwait(true);

        picked.Should().BeTrue("an unpicked room becomes picked");
        harness.Grain._state.RoomSnapshot.StaffPick.Should().BeTrue();

        bool? unpicked = await harness
            .Grain.ToggleStaffPickAsync(RoomHarness.Stranger, CancellationToken.None)
            .ConfigureAwait(true);

        unpicked.Should().BeFalse("a second click takes it back out");
        harness.Grain._state.RoomSnapshot.StaffPick.Should().BeFalse();
    }

    /// <summary>The dashboard knows the state it wants, so it sets rather than flips — two calls
    /// with the same value must not undo each other.</summary>
    [Fact]
    public async Task SettingTheStaffPick_IsIdempotent()
    {
        RoomHarness harness = await RoomHarness
            .CreateAsync(capabilities: [Capabilities.Navigator.StaffPick])
            .ConfigureAwait(true);

        await SeedRoomRowAsync(harness).ConfigureAwait(true);

        await harness
            .Grain.SetStaffPickAsync(RoomHarness.Stranger, staffPick: true, CancellationToken.None)
            .ConfigureAwait(true);
        await harness
            .Grain.SetStaffPickAsync(RoomHarness.Stranger, staffPick: true, CancellationToken.None)
            .ConfigureAwait(true);

        harness.Grain._state.RoomSnapshot.StaffPick.Should().BeTrue();
    }

    /// <summary>The shared harness seeds the room's furniture and bots but not the room row itself,
    /// and the staff pick is a column on that row.</summary>
    private static async Task SeedRoomRowAsync(RoomHarness harness)
    {
        await using VortexDbContext db = harness.NewDbContext();

        db.Rooms.Add(
            new RoomEntity
            {
                Id = RoomHarness.RoomIdValue,
                Name = "A room",
                Description = string.Empty,
                PlayerEntityId = RoomHarness.Owner.Value,
                PlayerEntity = null!,
                RoomModelEntityId = 1,
                RoomModelEntity = null!,
                DoorMode = RoomDoorModeType.Open,
                UsersNow = 0,
                PlayersMax = 25,
                TradeType = RoomTradeModeType.Disabled,
                WallHeight = -1,
                HideWalls = false,
                ThicknessWall = RoomThicknessType.Normal,
                ThicknessFloor = RoomThicknessType.Normal,
                AllowBlocking = false,
                AllowPets = true,
                AllowPetsEat = false,
                MuteType = ModSettingType.Owner,
                KickType = ModSettingType.Owner,
                BanType = ModSettingType.Owner,
                ChatModeType = ChatModeType.FreeFlow,
                ChatBubbleType = ChatBubbleWidthType.Normal,
                ChatSpeedType = ChatScrollSpeedType.Normal,
                ChatFloodType = ChatFloodSensitivityType.Minimal,
                ChatDistance = 50,
                Score = 0,
                IsStaffPick = false,
            }
        );

        await db.SaveChangesAsync().ConfigureAwait(true);
    }
}
