using System.Threading.Tasks;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Layers;
using Vortex.Rooms.Grains.Zones.Runtime;

namespace Vortex.Rooms.Grains;

public sealed partial class RoomGrain
{
    public Task<bool> ExpectArrivalAsync(PlayerId playerId, string entryId)
    {
        int max = _state.RoomSnapshot.PlayersMax;

        // The same test the entry makes (AdmitAsync).
        if (max > 0 && _state.AvatarsByPlayerId.Count >= max)
        {
            return Task.FromResult(false);
        }

        ZoneArrival.Expect(playerId, entryId);

        return Task.FromResult(true);
    }

    public Task<ZoneCellRules?> GetRulesAtAsync(int x, int y) =>
        Task.FromResult(_state.ZoneRuntime is { } zone ? zone.RulesAt(x, y) : (ZoneCellRules?)null);

    public async Task<RoomId?> InstanceForAsync(PlayerId playerId)
    {
        ZoneRuntimeState? zone = _state.ZoneRuntime;
        string group = ZoneInstanceGroups.KeyFor(playerId);

        // A copy id is easy to guess: only the group it was given to gets in, and a copy nobody
        // was given is nobody's.
        if (ZoneInstanceIds.IsInstance(_state.RoomId))
        {
            return await _grainFactory.GetZoneInstanceGrain().IsMemberAsync(_state.RoomId, group)
                ? _state.RoomId
                : null;
        }

        if (zone?.Compiled.Document.Settings.Type != ZoneType.Dungeon)
        {
            return _state.RoomId;
        }

        RoomId? copy = await _grainFactory.GetZoneInstanceGrain().EnterAsync(_state.RoomId, group);

        // A passage or a test announced the player here; it is the copy that receives them.
        if (
            copy is { } instance
            && zone.ExpectedArrivals.Remove(playerId, out ZoneExpectedArrival expected)
            && expected.ExpiresAtMs >= NowMs()
        )
        {
            IRoomZone room = _grainFactory.GetRoomZone(instance);

            if (expected.EntryId is { } entry)
            {
                await room.ExpectArrivalAsync(playerId, entry);
            }
            else
            {
                await room.ExpectArrivalAtAsync(playerId, expected.X, expected.Y);
            }
        }

        return copy;
    }

    public Task ExpectArrivalAtAsync(PlayerId playerId, int x, int y)
    {
        ZoneArrival.ExpectAt(playerId, x, y);

        return Task.CompletedTask;
    }
}
