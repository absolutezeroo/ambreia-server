using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Action;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;

namespace Vortex.Primitives.Rooms.Grains;

/// <summary>Who may do what in the room: effective controller level and the guild-derived rights
/// that feed it.</summary>
[Alias("Vortex.Primitives.Rooms.Grains.IRoomSecurity")]
public interface IRoomSecurity : IGrainWithIntegerKey
{
    /// <summary>
    /// Resolves <paramref name="ctx"/>'s effective rights in this room (owner/explicit
    /// rights/staff capabilities). Callers outside the grain (e.g. the room-entry flow) must go
    /// through this instead of approximating from a raw owner-id comparison.
    /// </summary>
    public Task<RoomControllerType> GetControllerLevelAsync(
        ActionContext ctx,
        CancellationToken ct
    );
}
