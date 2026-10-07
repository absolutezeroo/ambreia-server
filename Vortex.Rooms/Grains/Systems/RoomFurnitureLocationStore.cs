using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;

namespace Vortex.Rooms.Grains.Systems;

/// <summary>
/// Moves a furniture row into a room and back out again.
/// </summary>
/// <remarks>
/// <para>
/// A room's write-behind flush writes where furniture sits. It used to write who owns it and which
/// room it is in as well, which made every position tick an assertion of ownership: a batch queued
/// before an item left could put it back in the room, and back on its old owner, over a trade that
/// had already taken it.
/// </para>
/// <para>
/// So location moves at the moment it happens, and it moves conditionally -- the same shape the
/// trade and the marketplace use. The tick is left with the one thing it is for.
/// </para>
/// <para>
/// The predicates are the inventory loader's own: a row is free-standing when it belongs to the
/// player and sits in no room. <c>DeletedAt</c> is absent from both
/// because the global soft-delete query filter supplies it.
/// </para>
/// </remarks>
internal static class RoomFurnitureLocationStore
{
    /// <summary>
    /// Takes a free-standing item out of <paramref name="ownerId" />'s hand and into
    /// <paramref name="roomId" />.
    /// </summary>
    /// <returns>1 when the row moved, 0 when it was not the player's to place.</returns>
    public static Task<int> ClaimIntoRoomAsync(
        VortexDbContext dbCtx,
        int itemId,
        int ownerId,
        int roomId,
        CancellationToken ct
    ) =>
        dbCtx
            .Furnitures.Where(f =>
                f.Id == itemId && f.PlayerEntityId == ownerId && f.RoomEntityId == null
            )
            .ExecuteUpdateAsync(row => row.SetProperty(f => f.RoomEntityId, roomId), ct);

    /// <summary>
    /// Takes an item out of <paramref name="roomId" /> and into <paramref name="newOwnerId" />'s
    /// hand.
    /// </summary>
    /// <remarks>
    /// The owner moves with it because a pickup can hand the item to someone other than whoever
    /// owned it -- clearing a room gives each piece back to its owner, while a rights-holder
    /// tidying up keeps what they picked up, and the caller has already decided which.
    /// </remarks>
    /// <returns>1 when the row moved, 0 when it was no longer this room's to give.</returns>
    public static Task<int> ReleaseFromRoomAsync(
        VortexDbContext dbCtx,
        int itemId,
        int roomId,
        int newOwnerId,
        CancellationToken ct
    ) =>
        dbCtx
            .Furnitures.Where(f => f.Id == itemId && f.RoomEntityId == roomId)
            .ExecuteUpdateAsync(
                row =>
                    row.SetProperty(f => f.RoomEntityId, (int?)null)
                        .SetProperty(f => f.PlayerEntityId, newOwnerId),
                ct
            );
}
