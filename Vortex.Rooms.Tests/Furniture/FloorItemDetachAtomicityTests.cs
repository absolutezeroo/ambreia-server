using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives;
using Vortex.Primitives.Action;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Object.Logic.Furniture;
using Vortex.Rooms.Tests.Support;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Furniture;

/// <summary>
/// What the room is left holding when a detach does not finish.
/// </summary>
/// <remarks>
/// <para>
/// A detach takes the footprint off the map, tells the room, runs the item's own hooks, and only
/// then drops the identity and the index entry. The hooks are two awaits wide and they are
/// behaviour code — a logic can rewrite its <c>ExtraData</c> or tell the room its pile changed —
/// so any of them can throw.
/// </para>
/// <para>
/// When one did, the item was off the map and still in <c>ItemsById</c> and the logic index.
/// Anything holding that bucket kept acting on furniture that was no longer anywhere,
/// and the tile it had stood on had already forgotten it. This is the detach half of the same
/// finding as <see cref="FloorItemAttachAtomicityTests" />, and it outlived the attach fix.
/// </para>
/// </remarks>
public sealed class FloorItemDetachAtomicityTests
{
    private static readonly RoomObjectId Placed = new(52);

    [Fact]
    public async Task ADetachWhosePickupHookThrows_StillTakesTheItemOutOfTheRoom()
    {
        (RoomHarness harness, IRoomFloorItem item) = await RoomWithItemAsync(
                TestFloorItems.WorkingLogic,
                LogicThatFailsOnPickup
            )
            .ConfigureAwait(true);

        Func<Task> detach = () => DetachAsync(harness, item);

        await detach.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(true);

        harness.Grain._state.ItemsById.Should().NotContainKey(Placed);
        Violations(harness).Should().BeEmpty();
    }

    /// <summary>
    /// The first hook, which leaves even more undone behind it — the pickup hook never runs at all.
    /// </summary>
    [Fact]
    public async Task ADetachWhoseDetachHookThrows_StillTakesTheItemOutOfTheRoom()
    {
        (RoomHarness harness, IRoomFloorItem item) = await RoomWithItemAsync(
                TestFloorItems.WorkingLogic,
                LogicThatFailsOnDetach
            )
            .ConfigureAwait(true);

        Func<Task> detach = () => DetachAsync(harness, item);

        await detach.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(true);

        harness.Grain._state.ItemsById.Should().NotContainKey(Placed);
        Violations(harness).Should().BeEmpty();
    }

    /// <summary>
    /// The failure has to reach the caller. <c>RemoveItemByIdAsync</c> publishes the pickup after
    /// this returns, and a detach whose hooks did not finish must not be reported as a clean pickup.
    /// </summary>
    [Fact]
    public async Task AFailedDetach_IsStillReportedAsAFailure()
    {
        (RoomHarness harness, IRoomFloorItem item) = await RoomWithItemAsync(
                TestFloorItems.WorkingLogic,
                LogicThatFailsOnPickup
            )
            .ConfigureAwait(true);

        Func<Task> detach = () => DetachAsync(harness, item);

        await detach
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*hook*")
            .ConfigureAwait(true);
    }

    /// <summary>The control: a healthy detach leaves the room consistent too, so the tests above are
    /// not passing because nothing ever gets attached in the first place.</summary>
    [Fact]
    public async Task ASuccessfulDetach_LeavesTheRoomConsistent()
    {
        (RoomHarness harness, IRoomFloorItem item) = await RoomWithItemAsync(
                TestFloorItems.WorkingLogic,
                TestFloorItems.WorkingLogic
            )
            .ConfigureAwait(true);

        bool detached = await DetachAsync(harness, item).ConfigureAwait(true);

        detached.Should().BeTrue();
        harness.Grain._state.ItemsById.Should().NotContainKey(Placed);
        Violations(harness).Should().BeEmpty();
    }

    /// <summary>
    /// Placed with a working logic, then the logic is swapped for the one under test: an item whose
    /// logic refuses to attach never reaches the map, so the failure has to be armed after it is in.
    /// </summary>
    private static async Task<(RoomHarness Harness, IRoomFloorItem Item)> RoomWithItemAsync(
        Func<IFurnitureFloorLogic> attachWith,
        Func<IFurnitureFloorLogic> detachWith
    )
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        harness.LogicFactory = attachWith;

        IRoomFloorItem item = TestFloorItems.Movable(Placed, RoomHarness.Owner, 2, 2);

        await harness
            .Grain.FurniModule.PlaceFloorItemAsync(
                harness.ContextFor(RoomHarness.Owner),
                item,
                5,
                6,
                Rotation.North,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        item.SetLogic(detachWith());

        return (harness, item);
    }

    private static Task<bool> DetachAsync(RoomHarness harness, IRoomFloorItem item) =>
        harness.Grain.ObjectModule.RemoveObjectAsync(
            harness.ContextFor(RoomHarness.Owner),
            item,
            CancellationToken.None
        );

    private static IFurnitureFloorLogic LogicThatFailsOnPickup() =>
        FailingLogic(nameof(IFurnitureFloorLogic.OnPickupAsync));

    private static IFurnitureFloorLogic LogicThatFailsOnDetach() =>
        FailingLogic(nameof(IFurnitureFloorLogic.OnDetachAsync));

    private static IFurnitureFloorLogic FailingLogic(string hook) =>
        FakeProxy.Create<IFurnitureFloorLogic>(call =>
            call.Method.Name == hook
                ? Task.FromException(new InvalidOperationException($"the {hook} hook threw"))
                : call.Method.Name switch
                {
                    nameof(IFurnitureFloorLogic.CanWalk) => true,
                    nameof(IFurnitureFloorLogic.CanStack) => true,
                    nameof(IFurnitureFloorLogic.CanSit) => false,
                    nameof(IFurnitureFloorLogic.CanLay) => false,
                    nameof(IFurnitureFloorLogic.GetPostureOffset) => Altitude.Zero,
                    _ => null,
                }
        );

    private static IReadOnlyList<string> Violations(RoomHarness harness) =>
        RoomFurnitureInvariants.Violations(harness.Grain._state);
}
