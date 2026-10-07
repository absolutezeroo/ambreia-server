using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Rooms.Snapshots.Furniture;

namespace Vortex.Primitives.Rooms.Grains;

public interface IRoomPersistenceGrain : IGrainWithIntegerKey
{
    /// <summary>
    /// Queues where an item sits, for the write-behind tick.
    /// </summary>
    /// <remarks>
    /// It used to take a <c>remove</c> flag, which made this queue responsible for moving an item
    /// out of the room as well. It is not: a pickup moves the row itself, at the moment the player
    /// picks it up, and only the position follows on the tick.
    /// </remarks>
    public Task EnqueueDirtyItemAsync(
        RoomId roomId,
        RoomItemSnapshot snapshot,
        CancellationToken ct
    );
    public Task EnqueueDirtyItemsAsync(
        RoomId roomId,
        List<RoomItemSnapshot> snapshots,
        CancellationToken ct
    );
}
