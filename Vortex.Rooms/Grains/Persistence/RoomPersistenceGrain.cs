using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Entities.Furniture;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Snapshots.Furniture;
using Vortex.Rooms.Configuration;

namespace Vortex.Rooms.Grains.Persistence;

public sealed class RoomPersistenceGrain(
    IDbContextFactory<VortexDbContext> dbCtxFactory,
    IOptions<RoomConfig> roomConfig,
    ILogger<IRoomPersistenceGrain> logger
) : Grain, IRoomPersistenceGrain
{
    private readonly IDbContextFactory<VortexDbContext> _dbCtxFactory = dbCtxFactory;

    private readonly Dictionary<long, RoomItemSnapshot> _dirtyItems = [];
    private readonly ILogger<IRoomPersistenceGrain> _logger = logger;
    private readonly RoomConfig _roomConfig = roomConfig.Value;
    private IDisposable? _timer;

    /// <summary>
    /// Queues where an item sits. Not who owns it, and not which room it is in.
    /// </summary>
    /// <remarks>
    /// A pickup still comes through here, for the last thing the item was -- its final extra data,
    /// its rotation. The move itself is not this queue's business: it happened when the player
    /// picked the item up, as a conditional claim, and this snapshot arriving two seconds later
    /// says nothing about where the item lives by then.
    /// </remarks>
    public Task EnqueueDirtyItemAsync(
        RoomId roomId,
        RoomItemSnapshot snapshot,
        CancellationToken ct
    )
    {
        _dirtyItems[snapshot.ObjectId] = snapshot;

        return Task.CompletedTask;
    }

    public Task EnqueueDirtyItemsAsync(
        RoomId roomId,
        List<RoomItemSnapshot> snapshots,
        CancellationToken ct
    )
    {
        foreach (RoomItemSnapshot snapshot in snapshots)
        {
            _dirtyItems[snapshot.ObjectId] = snapshot;
        }

        return Task.CompletedTask;
    }

    public override Task OnActivateAsync(CancellationToken ct)
    {
        _timer = this.RegisterGrainTimer<object?>(
            static async (self, ct) =>
            {
                RoomPersistenceGrain grain = (RoomPersistenceGrain)self!;

                await grain.FlushDirtyItemsAsync(ct);
            },
            this,
            TimeSpan.FromMilliseconds(_roomConfig.DirtyItemsTickMs),
            TimeSpan.FromMilliseconds(_roomConfig.DirtyItemsTickMs)
        );

        return Task.CompletedTask;
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct)
    {
        // Drain rather than flush once. A flush writes at most MaxDirtyItemsPerFlush, which is the
        // right bound for a timer -- it stops one busy room from holding a database connection for a
        // whole build session -- and the wrong one here: a room deactivating with 300 moved items
        // wrote 100 of them and dropped the rest on the floor.
        //
        // Bounded by progress, not by a count: the loop stops the moment a pass fails to shrink the
        // queue, so a database that is refusing writes costs one extra attempt instead of spinning
        // through deactivation.
        while (_dirtyItems.Count > 0)
        {
            int before = _dirtyItems.Count;

            await FlushDirtyItemsAsync(ct);

            if (_dirtyItems.Count >= before)
            {
                break;
            }
        }
    }

    private async Task FlushDirtyItemsAsync(CancellationToken ct)
    {
        if (_dirtyItems.Count == 0)
        {
            return;
        }

        RoomItemSnapshot[] batch = _dirtyItems
            .Take(_roomConfig.MaxDirtyItemsPerFlush)
            .Select(x => x.Value)
            .ToArray();

        // Removed after the save, not before it. Taken off the queue up front, a batch that then
        // failed to save was gone: the catch below logged it and the positions were lost until
        // somebody moved the furniture again. One connection blip cost a room its layout, quietly.
        //
        // The grain is not reentrant, so nothing can enqueue during the await -- the queue this
        // returns to is the one it left.
        try
        {
            using VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct);

            // Where the furniture sits, and nothing else. This used to mark player_id and room_id
            // modified too, which made every position tick an assertion of ownership: a batch queued
            // before an item left could put it back in this room, and back on its old owner, over a
            // pickup or a placement that had already taken it (ROOM-PER-005, ECON-ITM-004). Location
            // moves at the moment it moves now, as a conditional claim -- see
            // RoomFurnitureLocationStore -- so a snapshot arriving two seconds late has no opinion
            // about where the item lives.
            //
            // "Its new room overwrites it on the next tick" was the reasoning, and it does not hold:
            // the new room only resends what is dirty, so an item that arrives and then sits still
            // is never rewritten. A snapshot queued before the item left could therefore land on it
            // minutes later and restore a position and an ExtraData from a room it is no longer in,
            // visible on the next reload with nothing to undo it.
            //
            // So the batch is filtered to the rows this room still holds, in one query. Per-row
            // conditional writes would be correct too and would cost the hundred round trips a tick
            // the batch exists to avoid; this keeps the single write and pays one read.
            //
            // ponytail: the window is now the gap between this read and the save below rather than a
            // whole tick. Closing it completely needs a placement generation carried in the snapshot
            // and checked in the write -- the audit's A2 -- which is a schema change and a pass over
            // every transfer path, not a line here.
            long roomId = this.GetPrimaryKeyLong();
            int[] batchIds = [.. batch.Select(b => b.ObjectId.Value)];
            HashSet<int> stillHere =
            [
                .. await dbCtx
                    .Furnitures.AsNoTracking()
                    .Where(f => batchIds.Contains(f.Id) && f.RoomEntityId == roomId)
                    .Select(f => f.Id)
                    .ToListAsync(ct),
            ];

            foreach (RoomItemSnapshot item in batch)
            {
                if (!stillHere.Contains(item.ObjectId.Value))
                {
                    // Gone to another room, back to a player's hand, or deleted. Whatever holds it now owns
                    // its position; this snapshot is a memory of somewhere else.
                    continue;
                }

                FurnitureEntity dbEntity = new()
                {
                    Id = item.ObjectId.Value,
                    PlayerEntityId = item.OwnerId.Value,
                    X = item.X,
                    Y = item.Y,
                    Z = item.Z,
                    Rotation = item.Rotation,
                    ExtraData = item.ExtraData,
                };

                dbCtx.Attach(dbEntity);

                EntityEntry<FurnitureEntity> e = dbCtx.Entry(dbEntity);

                e.Property(x => x.X).IsModified = true;
                e.Property(x => x.Y).IsModified = true;
                e.Property(x => x.Z).IsModified = true;
                e.Property(x => x.Rotation).IsModified = true;
                e.Property(x => x.ExtraData).IsModified = true;

                if (item is RoomWallItemSnapshot wallItem)
                {
                    dbEntity.WallOffset = wallItem.WallOffset;

                    e.Property(x => x.WallOffset).IsModified = true;
                }
            }

            await dbCtx.SaveChangesAsync(ct);

            foreach (RoomItemSnapshot item in batch)
            {
                _dirtyItems.Remove(item.ObjectId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to flush {Count} dirty furniture items for room {RoomId}",
                batch.Length,
                this.GetPrimaryKeyLong()
            );
        }
    }
}
