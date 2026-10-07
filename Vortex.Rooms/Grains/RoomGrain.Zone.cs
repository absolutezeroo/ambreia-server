using System.Threading.Tasks;
using Vortex.Primitives.Players;

namespace Vortex.Rooms.Grains;

public sealed partial class RoomGrain
{
    public Task ExpectArrivalAsync(PlayerId playerId, string entryId)
    {
        ZoneArrival.Expect(playerId, entryId);

        return Task.CompletedTask;
    }
}
