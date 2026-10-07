using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;

namespace Vortex.Primitives.World.Grains;

/// <summary>
/// The map editor's test mode (plan D18): which zone a tester's next sign-in starts in. One
/// activation for the silo, kept alive: a pending test outlives the tester's presence grain, which
/// is collected two minutes after their last socket closed.
/// </summary>
[Alias("Vortex.Primitives.World.Grains.IZoneTestGrain")]
public interface IZoneTestGrain : IGrainWithIntegerKey
{
    /// <summary>
    /// The next sign-in of <paramref name="playerId"/> starts in <paramref name="roomId"/>, on
    /// (<paramref name="x"/>, <paramref name="y"/>) when x is not negative, else on the zone's arrival.
    /// </summary>
    public Task ExpectAsync(PlayerId playerId, RoomId roomId, int x, int y);

    /// <summary>The room the player's sign-in starts in, once; null when no test is pending.</summary>
    public Task<RoomId?> TakeAsync(PlayerId playerId);
}
