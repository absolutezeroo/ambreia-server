using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Orleans.Concurrency;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Rooms.Events;

namespace Vortex.Primitives.Rooms.Grains;

/// <summary>Room activation lifecycle, the whole-room snapshots and the room-wide event/composer
/// fan-out — the facet every other room facet is layered on top of.</summary>
[Alias("Vortex.Primitives.Rooms.Grains.IRoomCore")]
public interface IRoomCore : IGrainWithIntegerKey
{
    // One-way, both of them. Their only cross-grain caller is the room directory's periodic sweep,
    // which touches every active room in the hotel and has nothing to do with the answer: awaiting
    // them held the directory's single cluster-wide activation for the length of the whole fan-out,
    // and -- because a room asking the directory anything then queued behind that -- gave two
    // non-reentrant grains a way to wait on each other until Orleans timed one of them out. Called
    // on a room by its own code (EnsureRoomActiveAsync, the settings path) these are plain method
    // calls and unaffected.
    [OneWay]
    public Task DeactivateRoomAsync();

    [OneWay]
    public Task DelayRoomDeactivationAsync();

    /// <summary>
    /// A new version of this room's zone was published: occupants are forwarded back into the room
    /// and the activation goes, so the next one loads the published document.
    /// </summary>
    public Task ReloadZoneAsync();
    public Task EnsureRoomActiveAsync(CancellationToken ct);

    // Pure reads: they never mutate _state, so letting them interleave with the room tick (and each
    // other) keeps them off the queue behind a 50ms tick instead of serializing on it.
    [AlwaysInterleave]
    [ReadOnly]
    public Task<RoomSnapshot> GetSnapshotAsync();

    [AlwaysInterleave]
    [ReadOnly]
    public Task<RoomSummarySnapshot> GetSummaryAsync();

    /// <summary>How many avatars are standing in the room.</summary>
    /// <remarks>
    /// Answered from the room's own occupants. It used to be a round trip to the cluster-wide room
    /// directory — a room asking somebody else how many people were standing in it — which put the
    /// hotel's single busiest serialization point on the path of the room's own summary.
    /// </remarks>
    [AlwaysInterleave]
    [ReadOnly]
    public Task<int> GetRoomPopulationAsync();

    [AlwaysInterleave]
    [ReadOnly]
    public Task<ImmutableArray<KeyValuePair<string, string>>> GetRoomPropertiesAsync();
    public Task PublishRoomEventAsync(RoomEvent evt, CancellationToken ct);
    public Task SendComposerToRoomAsync(IComposer composer);
}
