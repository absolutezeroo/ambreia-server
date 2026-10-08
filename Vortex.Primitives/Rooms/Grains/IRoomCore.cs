using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Orleans.Concurrency;
using Vortex.Primitives.Action;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Rooms.Events;

namespace Vortex.Primitives.Rooms.Grains;

/// <summary>Room activation lifecycle, the whole-room snapshots and the room-wide event/composer
/// fan-out — the facet every other room facet is layered on top of.</summary>
[Alias("Vortex.Primitives.Rooms.Grains.IRoomCore")]
public interface IRoomCore : IGrainWithIntegerKey
{
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

    /// <summary>
    /// The room's door (the population cap, the locked door, the password, the cancellable entry
    /// event) and, past it, the entry burst built from the room's own state in one turn. The
    /// player's presence grain sends it: it is the one that holds the socket and the room pointer.
    /// A <paramref name="resumed"/> player was admitted before the cut and skips the cap and the
    /// password.
    /// </summary>
    public Task<RoomAdmissionSnapshot> AdmitAsync(
        ActionContext ctx,
        string password,
        bool resumed,
        CancellationToken ct
    );

    // Pure reads: they never mutate _state, so letting them interleave with the room tick (and each
    // other) keeps them off the queue behind a 50ms tick instead of serializing on it.
    [AlwaysInterleave]
    [ReadOnly]
    public Task<RoomSnapshot> GetSnapshotAsync();

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
