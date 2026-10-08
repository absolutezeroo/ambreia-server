using System.Threading.Tasks;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.World;

namespace Vortex.Rpg.Characters.Grains;

/// <summary>Where the character is: the zone it entered, kept as the last one to sign in to
/// (spec Personnage §9).</summary>
internal sealed partial class PlayerCharacterGrain
{
    public async Task EnteredRoomAsync(RoomId roomId)
    {
        KeepAsLastRoom(roomId);

        await PushStateAsync();
    }

    public Task<RoomId?> GetLastRoomAsync() =>
        Task.FromResult(
            _character?.LastRoomId is int room && room > 0 ? (RoomId?)new RoomId(room) : null
        );

    private void KeepAsLastRoom(RoomId roomId)
    {
        // A dungeon copy is not somewhere to come back to (spec Personnage §9).
        if (
            _character is { } c
            && roomId.Value > 0
            && !ZoneInstanceIds.IsInstance(roomId)
            && c.LastRoomId != roomId.Value
        )
        {
            c.LastRoomId = roomId.Value;
            _dirty = true;
        }
    }
}
