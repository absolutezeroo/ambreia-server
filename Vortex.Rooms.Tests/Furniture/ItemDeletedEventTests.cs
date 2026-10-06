using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Database.Context;
using Vortex.Database.Entities.Furniture;
using Vortex.Primitives.Action;
using Vortex.Primitives.Events;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Rooms.Tests.Support;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Furniture;

/// <summary>
/// <see cref="ItemDeletedEvent" /> was declared, had a forensics handler waiting on it, and was
/// published by nothing: every cracked egg, opened present and binned sticky left the database
/// without a trace of who spent it. It is raised from the one method that stamps
/// <c>FurnitureEntity.DeletedAt</c>, so these two cases cover every consumption path there is.
/// </summary>
public sealed class ItemDeletedEventTests
{
    private const int ITEM_ID = 7788;

    [Fact]
    public async Task ConsumingAnItem_AnnouncesTheDeletionWithItsReason()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        await SeedFurnitureRowAsync(harness).ConfigureAwait(true);

        bool consumed = await harness
            .Grain.ConsumeItemAsync(
                harness.ContextFor(RoomHarness.Stranger),
                FakeItem(),
                ItemDeletionReason.PresentOpened,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        consumed.Should().BeTrue();

        ItemDeletedEvent deleted = harness
            .PublishedEvents.OfType<ItemDeletedEvent>()
            .Should()
            .ContainSingle()
            .Subject;

        deleted.ItemId.Should().Be(ITEM_ID);
        deleted.OwnerId.Should().Be(RoomHarness.Owner.Value);
        deleted.ActorPlayerId.Should().Be(RoomHarness.Stranger.Value);
        deleted.Reason.Should().Be(ItemDeletionReason.PresentOpened);
    }

    [Fact]
    public async Task AFailedDeleteAnnouncesNothing()
    {
        // No row to soft-delete, so the write throws and the method reports failure. The caller
        // relies on that to withhold the prize; a deletion event here would record a destruction
        // that never happened.
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        bool consumed = await harness
            .Grain.ConsumeItemAsync(
                harness.ContextFor(RoomHarness.Stranger),
                FakeItem(),
                ItemDeletionReason.PresentOpened,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        consumed.Should().BeFalse();
        harness.PublishedEvents.OfType<ItemDeletedEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task ConsumingTheSameItemTwice_DeletesItOnce()
    {
        // The soft delete used to be an unconditional attach: consuming an already-consumed item
        // stamped DeletedAt again, reported success, and let the caller hand out a second prize.
        // The guard that was supposed to stop it -- the item leaving ItemsById -- lands twenty lines
        // and two awaits after the write (RSYS-CONSUME-049).
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        await SeedFurnitureRowAsync(harness).ConfigureAwait(true);

        bool first = await ConsumeAsync(harness).ConfigureAwait(true);
        bool second = await ConsumeAsync(harness).ConfigureAwait(true);

        first.Should().BeTrue();
        second.Should().BeFalse();
        harness.PublishedEvents.OfType<ItemDeletedEvent>().Should().ContainSingle();
    }

    private static Task<bool> ConsumeAsync(RoomHarness harness) =>
        harness.Grain.ConsumeItemAsync(
            harness.ContextFor(RoomHarness.Stranger),
            FakeItem(),
            ItemDeletionReason.PresentOpened,
            CancellationToken.None
        );

    private static async Task SeedFurnitureRowAsync(RoomHarness harness)
    {
        await using VortexDbContext db = harness.NewDbContext();

        db.Furnitures.Add(
            new FurnitureEntity { Id = ITEM_ID, PlayerEntityId = RoomHarness.Owner.Value }
        );

        await db.SaveChangesAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// An item the room does not have on its map: <c>RemoveObjectAsync</c> then bails out before it
    /// touches the logic, which is what keeps this test to the deletion itself.
    /// </summary>
    private static IRoomItem FakeItem() =>
        FakeProxy.Create<IRoomItem>(call =>
            call.Method.Name switch
            {
                $"get_{nameof(IRoomItem.ObjectId)}" => new RoomObjectId(ITEM_ID),
                $"get_{nameof(IRoomItem.OwnerId)}" => RoomHarness.Owner,
                _ => null,
            }
        );
}
