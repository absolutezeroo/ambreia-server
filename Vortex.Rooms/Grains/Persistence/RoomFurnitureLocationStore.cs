using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;

namespace Vortex.Rooms.Grains.Persistence;

/// <summary>
/// Moves a furniture row into a room and back out again: the room's only database write outside
/// the write-behind flush (<see cref="RoomPersistenceGrain" />), so the modules never open a context.
/// </summary>
/// <remarks>
/// <para>
/// A room's write-behind flush writes where furniture sits. It used to write who owns it and which
/// room it is in as well, which made every position tick an assertion of ownership: a batch queued
/// before an item left could put it back in the room, and back on its old owner, after something
/// else had already moved it.
/// </para>
/// <para>
/// So location moves at the moment it happens, and it moves conditionally. The tick is left with
/// the one thing it is for.
/// </para>
/// <para>
/// A row is free-standing when it belongs to the player and sits in no room. <c>DeletedAt</c> is absent from both
/// because the global soft-delete query filter supplies it.
/// </para>
/// </remarks>
internal sealed class RoomFurnitureLocationStore(IDbContextFactory<VortexDbContext> dbCtxFactory)
{
    /// <summary>
    /// Takes a free-standing item out of <paramref name="ownerId" />'s hand and into
    /// <paramref name="roomId" />.
    /// </summary>
    /// <returns>True when the row moved, false when it was not the player's to place.</returns>
    public async Task<bool> ClaimIntoRoomAsync(
        int itemId,
        int ownerId,
        int roomId,
        CancellationToken ct
    )
    {
        await using VortexDbContext dbCtx = await dbCtxFactory.CreateDbContextAsync(ct);

        return await dbCtx
                .Furnitures.Where(f =>
                    f.Id == itemId && f.PlayerEntityId == ownerId && f.RoomEntityId == null
                )
                .ExecuteUpdateAsync(row => row.SetProperty(f => f.RoomEntityId, roomId), ct) > 0;
    }

    /// <summary>
    /// Takes an item out of <paramref name="roomId" /> and into <paramref name="newOwnerId" />'s
    /// hand.
    /// </summary>
    /// <remarks>
    /// The owner moves with it because a pickup can hand the item to someone other than whoever
    /// owned it -- the pickup type decides whether a piece goes back to its owner or to whoever
    /// picked it up, and the caller has already decided which.
    /// </remarks>
    /// <returns>True when the row moved, false when it was no longer this room's to give.</returns>
    public async Task<bool> ReleaseFromRoomAsync(
        int itemId,
        int roomId,
        int newOwnerId,
        CancellationToken ct
    )
    {
        await using VortexDbContext dbCtx = await dbCtxFactory.CreateDbContextAsync(ct);

        return await dbCtx
                .Furnitures.Where(f => f.Id == itemId && f.RoomEntityId == roomId)
                .ExecuteUpdateAsync(
                    row =>
                        row.SetProperty(f => f.RoomEntityId, (int?)null)
                            .SetProperty(f => f.PlayerEntityId, newOwnerId),
                    ct
                ) > 0;
    }
}
