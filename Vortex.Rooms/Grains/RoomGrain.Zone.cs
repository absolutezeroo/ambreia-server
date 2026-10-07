using System.Threading.Tasks;
using Vortex.Primitives.Players;

namespace Vortex.Rooms.Grains;

public sealed partial class RoomGrain
{
    public Task<bool> ExpectArrivalAsync(PlayerId playerId, string entryId)
    {
        int max = _state.RoomSnapshot.PlayersMax;

        // The same test the entry makes (RoomService.OpenRoomForPlayerIdAsync).
        if (max > 0 && _state.AvatarsByPlayerId.Count >= max)
        {
            return Task.FromResult(false);
        }

        ZoneArrival.Expect(playerId, entryId);

        return Task.FromResult(true);
    }

    public Task ExpectArrivalAtAsync(PlayerId playerId, int x, int y)
    {
        ZoneArrival.ExpectAt(playerId, x, y);

        return Task.CompletedTask;
    }
}
