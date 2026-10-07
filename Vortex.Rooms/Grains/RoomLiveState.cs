using System;
using System.Collections.Generic;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Snapshots.Mapping;
using Vortex.Primitives.World;
using Vortex.Rooms.Configuration;

namespace Vortex.Rooms.Grains;

public sealed class RoomLiveState
{
    public required RoomId RoomId { get; init; }
    public RoomSnapshot RoomSnapshot { get; set; } = default!;

    // Any new mutation path for ItemsById must also notify ItemIndex — see its invariant note.
    public Dictionary<RoomObjectId, IRoomItem> ItemsById { get; } = [];

    /// <summary>Items indexed by attached-logic type; kept in lockstep with
    /// <see cref="ItemsById"/> by the object module.</summary>
    public RoomItemIndex ItemIndex { get; } = new();
    public Dictionary<RoomObjectId, IRoomAvatar> AvatarsByObjectId { get; } = [];
    public Dictionary<PlayerId, RoomObjectId> AvatarsByPlayerId { get; } = [];
    public Dictionary<PlayerId, string> OwnerNamesById { get; } = [];

    public RoomModelSnapshot? Model { get; internal set; } = null;
    public Altitude[] TileHeights { get; internal set; } = [];
    public short[] TileEncodedHeights { get; internal set; } = [];
    public RoomTileFlags[] TileFlags { get; internal set; } = [];
    public RoomObjectId[] TileHighestFloorItems { get; internal set; } = [];
    public HashSet<RoomObjectId>[] TileFloorStacks { get; internal set; } = [];
    public HashSet<RoomObjectId>[] TileAvatarStacks { get; internal set; } = [];

    public HashSet<PlayerId> PlayerIdsWithRights { get; } = [];

    public Dictionary<string, string> RoomProperties { get; } = [];

    public HashSet<int> DirtyHeightTileIds { get; set; } = [];
    public HashSet<RoomObjectId> DirtyItemIds { get; set; } = [];
    public HashSet<RoomObjectId> DirtyFloorItemIds { get; set; } = [];
    public HashSet<RoomObjectId> DirtyWallItemIds { get; set; } = [];

    public bool IsMapReady { get; internal set; } = false;

    /// <summary>The published document this room runs when it is a zone; null otherwise.</summary>
    public ZoneDocument? Zone { get; internal set; }
    public bool IsFurniLoaded { get; internal set; } = false;
    public bool IsTileComputationPaused { get; internal set; } = false;

    public long EpochMs { get; set; } = 0;
    public long NextAvatarBoundaryMs { get; set; } = 0;
}
