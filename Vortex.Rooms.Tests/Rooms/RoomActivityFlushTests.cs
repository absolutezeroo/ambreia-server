using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Entities.Room;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Rooms.Configuration;
using Vortex.Rooms.Grains;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Rooms;

/// <summary>
/// <c>rooms.users_now</c> is written once, at creation, with a 0 — and the navigator orders
/// <em>and pages</em> on it in SQL.
/// </summary>
/// <remarks>
/// That made every room list in the hotel rank by rating alone, and a busy room the database still
/// believed was empty did not merely sort low: it fell out of the page. The category visitor counts,
/// which are a <c>SUM</c> of the same column, read 0 forever. The room directory is the only thing
/// that knows the real headcount, so it is the only thing that can tell the database.
/// <para>
/// Sqlite rather than the in-memory provider: the flush is built out of <c>ExecuteUpdate</c>, which
/// in-memory does not implement.
/// </para>
/// </remarks>
public sealed class RoomActivityFlushTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<VortexDbContext> _options;

    public RoomActivityFlushTests()
    {
        _conn = new SqliteConnection("Filename=:memory:");
        _conn.Open();

        _options = new DbContextOptionsBuilder<VortexDbContext>().UseSqlite(_conn).Options;

        using VortexDbContext seed = new(_options);

        seed.Database.EnsureCreated();

        // Seeding an owner and a model would mean seeding most of the schema to test two UPDATEs,
        // and referential integrity is not what is under test.
        seed.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF");
    }

    public void Dispose() => _conn.Dispose();

    [Fact]
    public async Task AFlush_WritesTheHeadcountTheDirectoryIsActuallyHolding()
    {
        SeedRoom(id: 1);

        RoomDirectoryGrain grain = NewGrain();

        await grain.AddPlayerToRoomAsync((PlayerId)10, (RoomId)1, CancellationToken.None);
        await grain.AddPlayerToRoomAsync((PlayerId)11, (RoomId)1, CancellationToken.None);

        await grain.FlushRoomActivityAsync(CancellationToken.None);

        RoomEntity room = await LoadAsync(1);

        room.UsersNow.Should().Be(2);
        room.LastActive.Should().BeAfter(DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task ARoomThatEmpties_IsWrittenBackToZero()
    {
        SeedRoom(id: 1, usersNow: 5);

        RoomDirectoryGrain grain = NewGrain();

        await grain.AddPlayerToRoomAsync((PlayerId)10, (RoomId)1, CancellationToken.None);
        await grain.RemovePlayerFromRoomAsync((PlayerId)10, (RoomId)1, CancellationToken.None);

        await grain.FlushRoomActivityAsync(CancellationToken.None);

        (await LoadAsync(1)).UsersNow.Should().Be(0);
    }

    /// <summary>A closed room is the moment its last persisted headcount became a lie, so the
    /// removal queues the write rather than dropping it with the in-memory entry.</summary>
    [Fact]
    public async Task ClosingARoom_QueuesItsHeadcountBackToZero()
    {
        SeedRoom(id: 1, usersNow: 5);

        RoomDirectoryGrain grain = NewGrain();

        await grain.RemoveActiveRoomAsync((RoomId)1);
        await grain.FlushRoomActivityAsync(CancellationToken.None);

        (await LoadAsync(1)).UsersNow.Should().Be(0);
    }

    [Fact]
    public async Task ARoomNobodyTouched_IsNotWritten()
    {
        SeedRoom(id: 1, usersNow: 5);
        SeedRoom(id: 2);

        RoomDirectoryGrain grain = NewGrain();

        await grain.AddPlayerToRoomAsync((PlayerId)10, (RoomId)2, CancellationToken.None);

        await grain.FlushRoomActivityAsync(CancellationToken.None);

        (await LoadAsync(1))
            .UsersNow.Should()
            .Be(5, "only the rooms whose occupancy changed are written");
    }

    /// <summary>One activation of this grain serves the whole cluster and it starts empty, so any
    /// occupancy already in the column belongs to a run that is over.</summary>
    [Fact]
    public async Task StartingUp_DropsOccupancyLeftBehindByAPreviousRun()
    {
        SeedRoom(id: 1, usersNow: 9);

        await NewGrain().ClearStaleOccupancyAsync(CancellationToken.None);

        (await LoadAsync(1)).UsersNow.Should().Be(0);
    }

    private RoomDirectoryGrain NewGrain() =>
        GrainActivationContext.CreateWithIntegerKey<RoomDirectoryGrain>(
            0,
            Options.Create(new RoomConfig()),
            NullLogger<IRoomDirectoryGrain>.Instance,
            FakeProxy.Create<IGrainFactory>(_ => null),
            new SqliteDbContextFactory(_options)
        );

    private async Task<RoomEntity> LoadAsync(int id)
    {
        await using VortexDbContext db = new(_options);

        return await db.Rooms.AsNoTracking().FirstAsync(r => r.Id == id);
    }

    /// <summary>Raw SQL: created_at is DatabaseGenerated(Identity) and updated_at is Computed, so EF
    /// writes neither and EnsureCreated leaves both NOT NULL with no default. Only the columns
    /// without one are listed; the flush is updates, which are unaffected.</summary>
    private void SeedRoom(int id, int usersNow = 0)
    {
        using VortexDbContext db = new(_options);

        db.Database.ExecuteSqlRaw(
            """
            INSERT INTO rooms (id, name, player_id, model_id, users_now, score, ranking,
                               last_active, created_at, updated_at)
            VALUES ({0}, {1}, 1, 1, {2}, 0, 0,
                    '2020-01-01 00:00:00', '2020-01-01 00:00:00', '2020-01-01 00:00:00')
            """,
            id,
            $"room-{id}",
            usersNow
        );
    }

    private sealed class SqliteDbContextFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);
    }
}
