using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Players;

namespace Vortex.Primitives.Rooms.Grains;

public interface IRoomDirectoryGrain : IGrainWithStringKey
{
    public Task<int> GetRoomPopulationAsync(RoomId roomId);

    public Task UpsertActiveRoomAsync(RoomInfoSnapshot snapshot);

    public Task RemoveActiveRoomAsync(RoomId roomId);

    public Task AddPlayerToRoomAsync(PlayerId playerId, RoomId roomId, CancellationToken ct);

    public Task RemovePlayerFromRoomAsync(PlayerId playerId, RoomId roomId, CancellationToken ct);
}
