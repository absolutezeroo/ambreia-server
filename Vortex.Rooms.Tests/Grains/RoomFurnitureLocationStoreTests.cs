using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Database.Entities.Furniture;
using Vortex.Rooms.Grains.Systems;
using Xunit;

namespace Vortex.Rooms.Tests.Grains;

/// <summary>
/// Where a piece of furniture lives, and who is allowed to say so.
/// </summary>
/// <remarks>
/// <para>
/// This used to be the write-behind tick's job, as a side effect of writing positions: every
/// snapshot marked player_id and room_id modified, so a batch queued before an item left could put
/// it back in the room and back on its old owner, over a trade that had already taken it.
/// </para>
/// <para>
/// It moves at the moment it moves now, and it moves conditionally. The half that shows up for
/// players is the release: six paths decide an item is free-standing by asking whether room_id is
/// null — a trade, a wired chest, a jukebox, a marketplace listing, a wired settlement — and while
/// the pickup waited for the tick, all six refused an item the client had already put in the
/// player's hand.
/// </para>
/// </remarks>
public sealed class RoomFurnitureLocationStoreTests : IAsyncLifetime
{
    private const int ROOM = 7;
    private const int OTHER_ROOM = 99;
    private const int OWNER = 1;
    private const int SOMEBODY_ELSE = 2;
    private const int ITEM = 500;

    private SqliteConnection _conn = null!;
    private DbContextOptions<VortexDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        _conn = new SqliteConnection("Filename=:memory:");
        await _conn.OpenAsync();
        _options = new DbContextOptionsBuilder<VortexDbContext>().UseSqlite(_conn).Options;

        await using VortexDbContext db = new(_options);

        // The same repair Vortex.Database.Tests makes: created_at is identity-generated and
        // updated_at computed, so EF writes neither, and the schema EF derives for SQLite leaves
        // both NOT NULL without the default MySQL's migrations carry.
        string script = Regex.Replace(
            db.Database.GenerateCreateScript(),
            """("(?:created_at|updated_at)"\s+[^\s,]+)\s+NOT NULL""",
            "$1 NOT NULL DEFAULT CURRENT_TIMESTAMP"
        );

        await db.Database.ExecuteSqlRawAsync(script);
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");

        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO furniture (id, player_id, definition_id, room_id, x, y, z, direction,
                                   wall_offset, extra_data, created_at, updated_at)
            VALUES ({0}, {1}, 1, NULL, 0, 0, 0, 0, 0, '', datetime('now'), datetime('now'))
            """,
            ITEM,
            OWNER
        );
    }

    public async Task DisposeAsync() => await _conn.DisposeAsync();

    [Fact]
    public async Task AFreeStandingItem_CanBeClaimedIntoTheRoom()
    {
        (await ClaimAsync(OWNER)).Should().Be(1);

        (await RowAsync()).RoomEntityId.Should().Be(ROOM);
    }

    /// <summary>Somebody else's item is not yours to place, however you got the packet there.</summary>
    [Fact]
    public async Task AnotherPlayersItem_IsNotClaimed()
    {
        (await ClaimAsync(SOMEBODY_ELSE)).Should().Be(0);

        (await RowAsync()).RoomEntityId.Should().BeNull();
    }

    /// <summary>
    /// An item already standing in a room is not claimable into a second one, which is what stops
    /// two rooms holding the same row.
    /// </summary>
    [Fact]
    public async Task AnItemAlreadyInARoom_IsNotClaimedIntoAnother()
    {
        (await ClaimAsync(OWNER)).Should().Be(1);

        await using VortexDbContext db = new(_options);

        (
            await RoomFurnitureLocationStore.ClaimIntoRoomAsync(
                db,
                ITEM,
                OWNER,
                OTHER_ROOM,
                CancellationToken.None
            )
        )
            .Should()
            .Be(0);
    }

    /// <summary>
    /// The release, and the reason the pickup does it itself: the row is free-standing the instant
    /// the player has the item in hand, rather than up to DirtyItemsTickMs later.
    /// </summary>
    [Fact]
    public async Task ReleasingFromTheRoom_HandsTheRowToThePicker()
    {
        await ClaimAsync(OWNER);

        (await ReleaseAsync(SOMEBODY_ELSE)).Should().Be(1);

        FurnitureEntity row = await RowAsync();

        row.RoomEntityId.Should().BeNull();
        row.PlayerEntityId.Should().Be(SOMEBODY_ELSE);
    }

    /// <summary>
    /// ROOM-PER-005 from the other side: a room can only release what it still holds. Pick a sofa
    /// up in A and drop it in B, and A's late release must not take it back out of B.
    /// </summary>
    [Fact]
    public async Task ARoomCannotReleaseAnItemAnotherRoomHasTaken()
    {
        await using (VortexDbContext db = new(_options))
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE furniture SET room_id = {0} WHERE id = {1}",
                OTHER_ROOM,
                ITEM
            );
        }

        (await ReleaseAsync(SOMEBODY_ELSE)).Should().Be(0);

        FurnitureEntity row = await RowAsync();

        row.RoomEntityId.Should().Be(OTHER_ROOM);
        row.PlayerEntityId.Should().Be(OWNER, "and the owner did not move either");
    }

    private async Task<int> ClaimAsync(int ownerId)
    {
        await using VortexDbContext db = new(_options);

        return await RoomFurnitureLocationStore.ClaimIntoRoomAsync(
            db,
            ITEM,
            ownerId,
            ROOM,
            CancellationToken.None
        );
    }

    private async Task<int> ReleaseAsync(int newOwnerId)
    {
        await using VortexDbContext db = new(_options);

        return await RoomFurnitureLocationStore.ReleaseFromRoomAsync(
            db,
            ITEM,
            ROOM,
            newOwnerId,
            CancellationToken.None
        );
    }

    private async Task<FurnitureEntity> RowAsync()
    {
        await using VortexDbContext db = new(_options);

        return await db.Furnitures.AsNoTracking().SingleAsync(f => f.Id == ITEM);
    }
}
