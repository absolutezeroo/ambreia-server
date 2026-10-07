using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Object.Furniture;

namespace Vortex.Primitives.Rooms.Object;

/// <summary>
/// Finding the other objects in the room. This is not a grain contract: room-object logic runs
/// inside the room's own turn, and these resolve against live state with no hop and no await --
/// which is exactly why nothing here returns a <c>Task</c>.
/// </summary>
public interface IRoomLookup
{
    /// <summary>The item with this object id, or null if the room has no such item.</summary>
    IRoomItem? FindItem(RoomObjectId objectId);

    /// <summary>The avatar with this object id, or null if the room has no such avatar.</summary>
    IRoomAvatar? FindAvatar(RoomObjectId objectId);

    /// <summary>The avatar this player is currently embodying, or null if they are not here.</summary>
    IRoomAvatar? FindAvatarByPlayer(PlayerId playerId);

    /// <summary>Dictionary-shaped <see cref="FindItem"/>, for call sites that read better as a
    /// guard than as a null test.</summary>
    bool TryFindItem(RoomObjectId objectId, [NotNullWhen(true)] out IRoomItem? item);

    /// <summary>Dictionary-shaped <see cref="FindAvatar(RoomObjectId)"/>.</summary>
    bool TryFindAvatar(RoomObjectId objectId, [NotNullWhen(true)] out IRoomAvatar? avatar);

    /// <summary>Dictionary-shaped <see cref="FindAvatarByPlayer"/>. Resolves player to avatar in
    /// one step; the room keeps the player-to-object-id indirection to itself.</summary>
    bool TryFindAvatarByPlayer(PlayerId playerId, [NotNullWhen(true)] out IRoomAvatar? avatar);

    /// <summary>Every item in the room. Live view -- do not hold it across an await.</summary>
    IReadOnlyCollection<IRoomItem> Items { get; }

    /// <summary>Every avatar in the room. Live view.</summary>
    IReadOnlyCollection<IRoomAvatar> Avatars { get; }

    /// <summary>How many players are in the room.</summary>
    int AvatarCount { get; }
}
