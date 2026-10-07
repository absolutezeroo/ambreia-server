using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Providers;
using Vortex.Rooms.Object.Avatars.Player;

namespace Vortex.Rooms.Providers;

public sealed class RoomAvatarProvider : IRoomAvatarProvider
{
    public IRoomPlayer CreateAvatarFromPlayerSnapshot(
        RoomObjectId objectId,
        PlayerSummarySnapshot snapshot
    )
    {
        RoomPlayerAvatar avatar = new RoomPlayerAvatar
        {
            ObjectId = objectId,
            PlayerId = snapshot.PlayerId,
            // Staff-ness is fixed for the visit, so it is set here rather than in
            // UpdateWithPlayer: the property stays init-only and a test can keep building an
            // avatar straight from an object initialiser.
            IsModerator = snapshot.IsModerator,
        };

        // UpdateWithPlayer stamps name, motto, figure, gender and score from the snapshot, so the
        // avatar enters the room with them rather than waiting for a player update.
        avatar.UpdateWithPlayer(snapshot);

        return avatar;
    }
}
