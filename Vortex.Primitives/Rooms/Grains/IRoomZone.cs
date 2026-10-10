using System.Threading.Tasks;
using Orleans;
using Orleans.Concurrency;
using Vortex.Primitives.Players;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Rules;

namespace Vortex.Primitives.Rooms.Grains;

/// <summary>What a published zone adds to its room: arrivals, dungeon copies, the rules of a cell.</summary>
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

    /// <summary>
    /// The room <paramref name="playerId"/> actually enters when they ask for this one: itself,
    /// or for a dungeon their group's copy, made if needed and given whatever arrival was expected
    /// here (plan D21). Null when every copy is taken.
    /// </summary>
    public Task<RoomId?> InstanceForAsync(PlayerId playerId);

    /// <summary>The same for a cell rather than an entry (the editor's test mode, plan D18).</summary>
    [AlwaysInterleave]
    public Task ExpectArrivalAtAsync(PlayerId playerId, int x, int y);

    /// <summary>
    /// What the cell (x, y) allows -- safe, PvP, hostile, mounts, restricted, level, music (plan
    /// D24). Null when the room is not a zone. What combat and monsters ask before they act.
    /// </summary>
    public Task<ZoneCellRules?> GetRulesAtAsync(int x, int y);

    /// <summary>
    /// Server side only: the player's presence passes on a part of what the zone knows of them
    /// that changed (spec Z1 §4.3). Ignored for a player not in the room, or older than the part
    /// the zone holds.
    /// </summary>
    public Task UpdatePlayerFactsAsync(PlayerId player, ZoneFacts facts);
}
