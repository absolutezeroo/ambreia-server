using System;
using System.Collections.Generic;
using Vortex.Primitives.Groups.Enums;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Pets.Snapshots;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Snapshots.Mapping;
using Vortex.Rooms.Configuration;

namespace Vortex.Rooms.Grains;

public sealed class RoomLiveState
{
    public required RoomId RoomId { get; init; }
    public RoomSnapshot RoomSnapshot { get; set; } = default!;

    // Any new mutation path for ItemsById must also notify ItemIndex — see its invariant note.
    public Dictionary<RoomObjectId, IRoomItem> ItemsById { get; } = [];

    /// <summary>Items indexed by attached-logic type; kept in lockstep with
    /// <see cref="ItemsById"/> by the object module and the two other removal sites.</summary>
    public RoomItemIndex ItemIndex { get; } = new();
    public Dictionary<RoomObjectId, IRoomAvatar> AvatarsByObjectId { get; } = [];
    public Dictionary<PlayerId, RoomObjectId> AvatarsByPlayerId { get; } = [];
    public Dictionary<int, PetSnapshot> PetsById { get; } = [];
    public Dictionary<PlayerId, string> OwnerNamesById { get; } = [];

    public RoomModelSnapshot? Model { get; internal set; } = null;
    public Altitude[] TileHeights { get; internal set; } = [];
    public short[] TileEncodedHeights { get; internal set; } = [];
    public RoomTileFlags[] TileFlags { get; internal set; } = [];
    public RoomObjectId[] TileHighestFloorItems { get; internal set; } = [];
    public HashSet<RoomObjectId>[] TileFloorStacks { get; internal set; } = [];
    public HashSet<RoomObjectId>[] TileAvatarStacks { get; internal set; } = [];

    public HashSet<PlayerId> PlayerIdsWithRights { get; } = [];

    /// <summary>The room's own banned words, hydrated on load. Case-insensitive because that is how
    /// the filter is applied — an owner who banned "bobba" did not mean to allow "BOBBA".</summary>
    public HashSet<string> FilterWords { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// What a filtered word is replaced with, cached from <c>room.filter.replacement</c>. Cached
    /// rather than read where it is used because that is the chat path, once per line. The default
    /// stands in for a read that never happened, so a room whose filter is live never masks a word
    /// with an empty string.
    /// </summary>
    public string FilterReplacement { get; set; } = RoomFilterConfig.ReplacementDefault;

    /// <summary>Member ranks for the guild that owns this room, keyed by player id. Empty when the
    /// room has no guild. Hydrated on room load and kept in sync by
    /// <c>RoomGrain.RefreshGroupMembershipAsync</c> — <see cref="Modules.RoomSecurityModule"/> reads
    /// it for every controller-level check, so it must never go stale while the room is active.</summary>
    public Dictionary<PlayerId, GroupMemberRank> GroupMemberRanks { get; } = [];

    /// <summary>Mirrors the owning guild's <c>admin_only_decoration</c> setting: when true, plain
    /// guild members get no build rights in the base room.</summary>
    public bool GroupAdminOnlyDecoration { get; internal set; } = false;

    /// <summary>Room-scoped mutes, from <c>room_mutes</c> — this room's own rule, hydrated on
    /// activation and cleared when the room deactivates.</summary>
    public Dictionary<PlayerId, DateTime> MuteExpiresUtc { get; } = [];

    /// <summary>
    /// Hotel-wide staff mutes for the players currently in this room. Seeded from each player's
    /// entry snapshot and updated live when a moderator mutes somebody standing here, so the chat
    /// path can enforce it without touching the database. Kept apart from
    /// <see cref="MuteExpiresUtc"/> on purpose: a room owner unmuting in their own room must not
    /// be able to lift a staff sanction.
    /// </summary>
    public Dictionary<PlayerId, DateTime> HotelMuteExpiresUtc { get; } = [];

    /// <summary>
    /// The room-info panel's "mute all" switch: while it is on, only the room's rights-holders can
    /// speak. Deliberately not persisted — like the reference emulator's, it is a live switch that
    /// lasts as long as the room stays loaded, so a room can never be left silent forever by
    /// somebody who forgot to turn it off.
    /// </summary>
    public bool AllInRoomMuted { get; internal set; }

    /// <summary>
    /// Raid protection: the owner's settings, the sliding window of arrivals, and whether an
    /// incident or a guard period is running. Loaded lazily on the first arrival of this activation
    /// — most rooms never configure it and should not pay a query for it.
    /// </summary>
    internal RoomRaidProtectionState RaidProtection { get; } = new();

    /// <summary>Players currently waiting on a doorbell ring for a locked door, keyed by the tick
    /// timestamp (ms) the ring started — swept for timeout by <c>RoomGrain.Doorbell.cs</c>.</summary>
    public Dictionary<PlayerId, long> PendingDoorbellRingersMs { get; } = [];

    /// <summary>Active trade sessions keyed by each participant's player id — both traders map to the
    /// same <see cref="RoomTradeSession"/> instance. Managed by <c>RoomGrain.Trading.cs</c>.</summary>
    internal Dictionary<PlayerId, RoomTradeSession> TradeSessionsByPlayerId { get; } = [];

    /// <summary>Mystery boxes waiting for their other half, keyed by the box's owner — the only
    /// identifier the client's cancel message carries. Managed by <c>RoomGrain.MysteryBox.cs</c>.</summary>
    internal Dictionary<PlayerId, RoomMysteryBoxSession> MysteryBoxSessionsByOwnerId { get; } = [];

    public Dictionary<string, string> RoomProperties { get; } = [];

    public HashSet<int> DirtyHeightTileIds { get; set; } = [];
    public HashSet<RoomObjectId> DirtyItemIds { get; set; } = [];
    public HashSet<RoomObjectId> DirtyFloorItemIds { get; set; } = [];
    public HashSet<RoomObjectId> DirtyWallItemIds { get; set; } = [];

    public bool IsMapReady { get; internal set; } = false;
    public bool IsFurniLoaded { get; internal set; } = false;
    public bool IsPetsLoaded { get; internal set; } = false;
    public bool IsTileComputationPaused { get; internal set; } = false;

    public long EpochMs { get; set; } = 0;
    public long NextAvatarBoundaryMs { get; set; } = 0;
    public long NextPetBoundaryMs { get; set; } = 0;
    public long NextBotBoundaryMs { get; set; } = 0;
    public long NextRollerBoundaryMs { get; set; } = 0;
}
