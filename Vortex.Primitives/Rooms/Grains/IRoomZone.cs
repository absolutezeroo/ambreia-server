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
    /// False, and nothing recorded, when the zone is full: the caller keeps the player rather than
    /// forward them to an entry that will refuse them.
    /// </summary>
    [AlwaysInterleave]
    public Task<bool> ExpectArrivalAsync(PlayerId playerId, string entryId);

    /// <summary>The same for a cell rather than an entry (the editor's test mode, plan D18).</summary>
    [AlwaysInterleave]
    public Task ExpectArrivalAtAsync(PlayerId playerId, int x, int y);
}
