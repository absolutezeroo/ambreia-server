using System.Collections.Generic;
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
/// What happens to the contents of a room when the room goes away.
/// </summary>
/// <remarks>
/// <para>
/// Nothing, was the answer. Deleting a room stamped <c>deleted_at</c> on the row, walked the
/// occupants out and stopped there, while every piece of furniture, every pet and every bot in it
/// kept pointing at a room the soft-delete filter now hides. The inventory loader only lists rows
/// whose room is null, and the room can never be opened again, so the whole contents went with it —
/// for every owner who had ever placed something there, not just the one who pressed delete, and on
/// an action the client offers as ordinary housekeeping.
/// </para>
/// <para>
/// The owner does not move: a deleted room gives each piece back to whoever owns it. That is the
/// difference from a pickup, where a rights-holder tidying up keeps what they lift.
/// </para>
/// </remarks>
public sealed class RoomContentsReleaseTests : IAsyncLifetime
{
    private const int ROOM = 7;
    private const int OTHER_ROOM = 99;
    private const int OWNER = 1;
    private const int GUEST = 2;

    private SqliteConnection _conn = null!;
    private DbContextOptions<VortexDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        _conn = new SqliteConnection("Filename=:memory:");
        await _conn.OpenAsync();
        _options = new DbContextOptionsBuilder<VortexDbContext>().UseSqlite(_conn).Options;

        await using VortexDbContext db = new(_options);

        // The same repair the sibling store tests make: created_at is identity-generated and
        // updated_at computed, so EF writes neither, and the schema EF derives for SQLite leaves
        // both NOT NULL without the default MySQL's migrations carry.
        string script = Regex.Replace(
            db.Database.GenerateCreateScript(),
            """("(?:created_at|updated_at)"\s+[^\s,]+)\s+NOT NULL""",
            "$1 NOT NULL DEFAULT CURRENT_TIMESTAMP"
        );

        await db.Database.ExecuteSqlRawAsync(script);
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");

        // Two owners in the doomed room -- the guest's chair is the one the old code lost silently,
        // since nothing in the delete path ever looked past the room's owner.
        await db.Furnitures.AddRangeAsync(
            Furni(id: 500, owner: OWNER, room: ROOM),
            Furni(id: 501, owner: GUEST, room: ROOM),
            Furni(id: 502, owner: OWNER, room: OTHER_ROOM)
        );

        await db.SaveChangesAsync();

        // Raw, because a pet carries a dozen required columns and a required navigation property
        // that say nothing about where it lives, which is the only column under test.
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO pets (id, player_id, room_id, name, type, skin, color, gender, level,
                              experience, x, y, z, direction,
                              created_at, updated_at)
            VALUES (600, {0}, {1}, 'Rex', 0, 0, 'FFFFFF', 0, 1, 0,
                    0, 0, 0, 0, datetime('now'), datetime('now'))
            """,
            GUEST,
            ROOM
        );

        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO bots (id, player_id, room_id, name, motto, figure, gender, x, y, z,
                              rotation, created_at, updated_at)
            VALUES (700, {0}, {1}, 'Greeter', '', 'hd-180-1', 0, 0, 0, 0, 0,
                    datetime('now'), datetime('now'))
            """,
            OWNER,
            ROOM
        );
    }

    public async Task DisposeAsync() => await _conn.DisposeAsync();

    [Fact]
    public async Task DeletingARoom_HandsEveryPieceBackToItsOwner()
    {
        await ReleaseAsync();

        await using VortexDbContext db = new(_options);

        FurnitureEntity ownerChair = await db
            .Furnitures.AsNoTracking()
            .SingleAsync(f => f.Id == 500);
        FurnitureEntity guestChair = await db
            .Furnitures.AsNoTracking()
            .SingleAsync(f => f.Id == 501);

        ownerChair.RoomEntityId.Should().BeNull();
        ownerChair.PlayerEntityId.Should().Be(OWNER);
        guestChair.RoomEntityId.Should().BeNull();
        guestChair
            .PlayerEntityId.Should()
            .Be(
                GUEST,
                "a deleted room returns each piece to its own owner, not to whoever deleted"
            );
    }

    /// <summary>Pets and bots are their own tables and were stranded the same way.</summary>
    [Fact]
    public async Task DeletingARoom_AlsoEmptiesPetsAndBots()
    {
        await ReleaseAsync();

        await using VortexDbContext db = new(_options);

        (await db.Pets.AsNoTracking().SingleAsync(p => p.Id == 600)).RoomEntityId.Should().BeNull();
        (await db.Bots.AsNoTracking().SingleAsync(b => b.Id == 700)).RoomEntityId.Should().BeNull();
    }

    /// <summary>
    /// The statement is bounded by the room. A neighbouring room's furniture is not this delete's
    /// business, and an unbounded release would empty the hotel.
    /// </summary>
    [Fact]
    public async Task AnotherRoomsFurniture_IsUntouched()
    {
        await ReleaseAsync();

        await using VortexDbContext db = new(_options);

        (await db.Furnitures.AsNoTracking().SingleAsync(f => f.Id == 502))
            .RoomEntityId.Should()
            .Be(OTHER_ROOM);
    }

    /// <summary>
    /// The caller refreshes the inventory views of exactly these players. Miss one and their rows
    /// are back in hand in the database while the loaded view still predates the release, so the
    /// items stay invisible until they relog -- the same symptom, one layer up.
    /// </summary>
    [Fact]
    public async Task EveryOwnerWithSomethingInTheRoom_IsReported()
    {
        (await ReleaseAsync()).Should().BeEquivalentTo([OWNER, GUEST]);
    }

    private async Task<List<int>> ReleaseAsync()
    {
        await using VortexDbContext db = new(_options);

        return await RoomFurnitureLocationStore.ReleaseRoomContentsAsync(
            db,
            ROOM,
            CancellationToken.None
        );
    }

    private static FurnitureEntity Furni(int id, int owner, int room) =>
        new()
        {
            Id = id,
            PlayerEntityId = owner,
            FurnitureDefinitionEntityId = 1,
            RoomEntityId = room,
            ExtraData = string.Empty,
        };
}
