using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Entities.Furniture;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Furniture.Snapshots.StuffData;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Snapshots.Furniture;
using Vortex.Rooms.Configuration;
using Vortex.Rooms.Grains.Persistence;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Grains;

/// <summary>
/// What a room can lose, and for how long.
/// </summary>
/// <remarks>
/// <para>
/// Furniture positions are memory-first: moving a sofa writes to the room's own state and queues a
/// snapshot, which <see cref="RoomPersistenceGrain"/> writes every <c>DirtyItemsTickMs</c>. A crash
/// inside that window costs the moves made in it, and that is a deliberate trade — a database write
/// per drag would put the hotel's furniture on the hot path of every build session.
/// </para>
/// <para>
/// Two things were not part of that trade. A flush whose save failed had already taken its batch off
/// the queue, so a connection blip lost those positions permanently rather than for two seconds; and
/// a deactivating room wrote one batch and abandoned the rest, so any room with more than
/// <c>MaxDirtyItemsPerFlush</c> pending moves lost the overflow every time it went to sleep. Both are
/// unbounded loss dressed up as a bounded window, which is the version nobody notices.
/// </para>
/// </remarks>
public sealed class RoomPersistenceLossWindowTests
{
    private const long ROOM = 7;

    [Fact]
    public async Task AFailedFlushKeepsItsBatchForTheNextOne()
    {
        using Harness h = new(maxPerFlush: 100);

        await h.EnqueueAsync(1);

        h.Fail = true;
        await h.DeactivateAsync();

        h.RowsWritten().Should().Be(0, "the save failed");

        // The regression: the batch used to be dropped from the queue before the try, so this second
        // attempt had nothing left to write and the move was gone for good.
        h.Fail = false;
        await h.DeactivateAsync();

        h.RowsWritten().Should().Be(1);
    }

    /// <summary>
    /// Deactivation drains, rather than writing one batch and abandoning the rest. The cap belongs to
    /// the timer — it stops one busy room holding a connection through a whole build session — and
    /// has no business deciding how much of a closing room survives.
    /// </summary>
    [Fact]
    public async Task DeactivationDrainsEveryPendingBatch()
    {
        using Harness h = new(maxPerFlush: 2);

        // Through the room's own tick path, which is how a batch this size actually arrives.
        await h.EnqueueBatchAsync(1, 2, 3, 4, 5);
        await h.DeactivateAsync();

        h.RowsWritten().Should().Be(5);
    }

    /// <summary>
    /// And it stops. A drain that loops until the queue empties would spin forever against a database
    /// that is refusing writes — during deactivation, which is where a silo is least able to say so.
    /// </summary>
    [Fact]
    public async Task DeactivationAgainstADeadDatabaseStopsAfterOnePass()
    {
        using Harness h = new(maxPerFlush: 2) { Fail = true };

        await h.EnqueueAsync(1, 2, 3, 4, 5);

        Func<Task> deactivate = () => h.DeactivateAsync();

        await deactivate.Should().CompleteWithinAsync(TimeSpan.FromSeconds(5));
        h.RowsWritten().Should().Be(0);
    }

    /// <summary>
    /// The tick writes where furniture sits and nothing else. It used to mark player_id and room_id
    /// modified on every snapshot, which made a position flush an assertion of ownership — a batch
    /// queued before an item left could put it back in this room and back on its old owner. Where an
    /// item lives is claimed when it moves now; see <c>RoomFurnitureLocationStoreTests</c>.
    /// </summary>
    [Fact]
    public async Task AFlush_DoesNotTouchOwnershipOrLocation()
    {
        using Harness h = new(maxPerFlush: 100);

        h.ClaimByAnotherRoom(1, room: 99);

        await h.EnqueueAsync(1);
        await h.DeactivateAsync();

        // This assertion is the reverse of what it used to be, deliberately. It read "the position
        // was still written", and writing it is the defect: the snapshot holds coordinates from
        // THIS room, and room 99 now holds the item. Landing them on it moves somebody else's
        // furniture to a spot chosen by a room it is no longer in, and the old reasoning for
        // allowing it -- that the new room rewrites it on its next tick -- does not hold, because a
        // room only resends what is dirty and an item that arrives and sits still never is.
        h.RowsWritten()
            .Should()
            .Be(0, "another room holds it; its position is not this room's");
        h.RoomOf(1).Should().Be(99, "the room that holds the row keeps it");
        h.OwnerOf(1).Should().Be(55, "and the owner it was traded to keeps it");
    }

    /// <remarks>
    /// SQLite rather than the InMemory provider, which every other grain suite here uses. The
    /// removal is a conditional claim -- one UPDATE carrying its own guard -- and InMemory does not
    /// implement <c>ExecuteUpdate</c>: it applied nothing and said nothing, so the two removal tests
    /// went green against a flush that had written no rows at all. A suite about a skin between two
    /// writers needs a provider that can express the write.
    /// </remarks>
    private sealed class Harness : IDisposable
    {
        private readonly SqliteConnection _conn;
        private readonly RoomPersistenceGrain _grain;
        private readonly DbContextOptions<VortexDbContext> _options;

        /// <summary>Makes the context factory throw, which is what a database blip looks like here.</summary>
        public bool Fail { get; set; }

        public Harness(int maxPerFlush)
        {
            _conn = new SqliteConnection("Filename=:memory:");
            _conn.Open();

            _options = new DbContextOptionsBuilder<VortexDbContext>().UseSqlite(_conn).Options;

            using (VortexDbContext seed = new(_options))
            {
                seed.Database.EnsureCreated();

                // Seeding a definition and a player would mean seeding most of the schema to test
                // one UPDATE guard, and referential integrity is not what is under test.
                seed.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF");

                // Raw SQL because created_at is DatabaseGenerated(Identity) and updated_at is
                // Computed, so EF writes neither and EnsureCreated leaves both NOT NULL with no
                // default. Updates are unaffected, which is all the flush does afterwards.
                //
                // The flush issues updates, so the rows have to exist, and they start IN the room
                // because that is what an item this room is flushing is: the location store claims
                // room_id when the item is placed and clears it on pickup. "Was it written" stays
                // answerable through x, which every seeded row starts at 0 and every snapshot
                // carries as its own id.
                for (int id = 1; id <= 8; id++)
                {
                    seed.Database.ExecuteSqlRaw(
                        """
                        INSERT INTO furniture (id, player_id, definition_id, room_id, x, y, z,
                                               direction, wall_offset, extra_data,
                                               created_at, updated_at)
                        VALUES ({0}, 1, 1, {1}, 0, 0, 0, 0, 0, '', datetime('now'), datetime('now'))
                        """,
                        id,
                        ROOM
                    );
                }
            }

            _grain = GrainActivationContext.CreateWithIntegerKey<RoomPersistenceGrain>(
                ROOM,
                FakeProxy.Create<IDbContextFactory<VortexDbContext>>(call =>
                    Fail ? throw new InvalidOperationException("database unavailable")
                    : call.Method.Name == nameof(IDbContextFactory<VortexDbContext>.CreateDbContext)
                        ? new VortexDbContext(_options)
                    : Task.FromResult(new VortexDbContext(_options))
                ),
                Options.Create(
                    new RoomConfig { DirtyItemsTickMs = 2000, MaxDirtyItemsPerFlush = maxPerFlush }
                ),
                NullLogger<IRoomPersistenceGrain>.Instance
            );
        }

        public async Task EnqueueAsync(params int[] objectIds)
        {
            foreach (int objectId in objectIds)
            {
                await _grain.EnqueueDirtyItemAsync(
                    new RoomId((int)ROOM),
                    Snapshot(objectId),
                    CancellationToken.None
                );
            }
        }

        /// <summary>
        ///     Deactivation, which is the only flush reachable from outside the grain — the timer never
        ///     fires outside a silo. It drains, so it exercises the timer's flush as well.
        /// </summary>
        public Task DeactivateAsync() => _grain.OnDeactivateAsync(Reason(), CancellationToken.None);

        /// <summary>
        /// The room's own tick path: one batch of snapshots of items that are in the room. Separate
        /// from <see cref="EnqueueAsync" /> because it is a separate method on the grain, and the
        /// removal marker had to be cleared on both.
        /// </summary>
        public Task EnqueueBatchAsync(params int[] objectIds) =>
            _grain.EnqueueDirtyItemsAsync(
                new RoomId((int)ROOM),
                [.. objectIds.Select(Snapshot)],
                CancellationToken.None
            );

        /// <summary>Another room getting there first, and taking the owner with it — which is what a
        /// pickup or a placement elsewhere leaves behind while this room's tick is still queued.</summary>
        public void ClaimByAnotherRoom(int objectId, int room, int owner = 55)
        {
            using VortexDbContext db = new(_options);

            FurnitureEntity row = db.Set<FurnitureEntity>().Single(f => f.Id == objectId);

            row.RoomEntityId = room;
            row.PlayerEntityId = owner;

            db.SaveChanges();
        }

        public int? RoomOf(int objectId)
        {
            using VortexDbContext db = new(_options);

            return db.Set<FurnitureEntity>().Single(f => f.Id == objectId).RoomEntityId;
        }

        public int OwnerOf(int objectId)
        {
            using VortexDbContext db = new(_options);

            return db.Set<FurnitureEntity>().Single(f => f.Id == objectId).PlayerEntityId;
        }

        /// <summary>
        /// How many rows the tick actually wrote. The seeded rows all start at x = 0 and every
        /// snapshot carries <c>X = objectId</c>, so a row whose x matches its id is one the flush
        /// reached.
        /// </summary>
        public int RowsWritten()
        {
            using VortexDbContext db = new(_options);

            return db.Set<FurnitureEntity>().Count(f => f.X == f.Id);
        }

        private static DeactivationReason Reason() =>
            new(DeactivationReasonCode.ShuttingDown, "test");

        private static RoomFloorItemSnapshot Snapshot(int objectId) =>
            new()
            {
                ObjectId = new RoomObjectId(objectId),
                OwnerId = new PlayerId(1),
                OwnerName = "owner",
                DefinitionId = 1,
                SpriteId = 1,
                X = objectId,
                Y = objectId,
                Z = Altitude.FromInt(0),
                Rotation = Rotation.North,
                StackHeight = Altitude.FromInt(1),
                StuffData = new EmptyStuffSnapshot { StuffBitmask = 0 },
                ExtraData = "",
                UsagePolicy = FurnitureUsageType.Nobody,
            };

        /// <summary>An in-memory SQLite database lives exactly as long as its connection.</summary>
        public void Dispose() => _conn.Dispose();
    }
}
