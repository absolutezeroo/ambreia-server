using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Action;

namespace Vortex.Primitives.Rooms.Grains;

/// <summary>What each player in the room aims at (spec F3b §6): kept by the room, private to the
/// player, cleared when the target leaves; runtime only, never stored.</summary>
[Alias("Vortex.Primitives.Rooms.Grains.IRoomTargets")]
public interface IRoomTargets : IGrainWithIntegerKey
{
    /// <summary>The acting player aims at <paramref name="objectId"/> — an avatar in this room,
    /// oneself included — or clears the target with 0 or any id that is not one; the player is
    /// answered <c>RpgTarget</c>.</summary>
    public Task SelectTargetAsync(ActionContext ctx, int objectId, CancellationToken ct);
}
