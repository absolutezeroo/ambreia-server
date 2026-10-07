using System.Threading.Tasks;
using Orleans;
using Orleans.Concurrency;
using Vortex.Primitives.Players;

namespace Vortex.Primitives.Rooms.Grains;

/// <summary>What a published zone adds to its room: where a player is expected to arrive.</summary>
[Alias("Vortex.Primitives.Rooms.Grains.IRoomZone")]
public interface IRoomZone : IGrainWithIntegerKey
{
    /// <summary>
    /// The next time <paramref name="playerId"/> enters, they arrive on the entry
    /// <paramref name="entryId"/> instead of the default one (a passage, a teleport). Forgotten
    /// after one use or <c>RoomConfig.ExpectedArrivalTimeoutMs</c>; an unknown entry is ignored.
    /// </summary>
    [AlwaysInterleave]
    public Task ExpectArrivalAsync(PlayerId playerId, string entryId);
}
