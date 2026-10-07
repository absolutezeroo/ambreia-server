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
            // UpdateWithPlayer: the property stays init-only and the chat-flood tests can keep
            // building an avatar straight from an object initialiser. It was never assigned at
            // all before, which left every staff member flood-gated like a stranger and made the
            // Builders Membership guest check treat them as an outsider.
            IsModerator = snapshot.IsModerator,
        };

        // UpdateWithPlayer also stamps the favourite-guild badge from the snapshot, so the avatar
        // enters the room already wearing it rather than staying unbadged until a manual toggle.
        avatar.UpdateWithPlayer(snapshot);

        return avatar;
    }
}
