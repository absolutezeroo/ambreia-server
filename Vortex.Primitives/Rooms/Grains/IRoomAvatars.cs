using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Action;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Snapshots.Avatars;

namespace Vortex.Primitives.Rooms.Grains;

/// <summary>Avatars present in the room: their lifecycle, movement and posture.</summary>
[Alias("Vortex.Primitives.Rooms.Grains.IRoomAvatars")]
public interface IRoomAvatars : IGrainWithIntegerKey
{
    public Task<bool> CreateAvatarFromPlayerAsync(
        ActionContext ctx,
        PlayerSummarySnapshot snapshot,
        CancellationToken ct
    );
    public Task<bool> RemoveAvatarFromPlayerAsync(
        ActionContext ctx,
        PlayerId playerId,
        CancellationToken ct
    );
    public Task<bool> WalkAvatarToAsync(
        ActionContext ctx,
        int targetX,
        int targetY,
        int? targetZKey,
        CancellationToken ct
    );
    public Task<bool> UpdateAvatarWithPlayerAsync(
        PlayerSummarySnapshot snapshot,
        CancellationToken ct
    );

    public Task<ImmutableArray<RoomAvatarSnapshot>> GetAllAvatarSnapshotsAsync(
        CancellationToken ct
    );

    public Task<bool> LookToAvatarAsync(
        ActionContext ctx,
        int targetX,
        int targetY,
        CancellationToken ct
    );
}
