using Vortex.Primitives.Networking.Revisions;
using Vortex.Revisions.Revision20260701.Maps;

namespace Vortex.Revisions.Revision20260701;

public sealed class Revision20260701()
    : RevisionBase(
        new IRevisionMap[]
        {
            new AvatarMap(),
            new HandshakeMap(),
            new NotificationsMap(),
            new RoomMap(),
            new AvailabilityMap(),
            new RpgCharactersMap(),
            new RpgItemsMap(),
            new RpgInteractionMap(),
            new RpgChatMap(),
            new RpgEmoteMap(),
        }
    )
{
    // The dev revision: the ids of Headers.cs as typed. A production server refuses it (its ids are
    // in this repository) and speaks a ReleaseRevision drawn from them instead.
    public override string Revision => "AMBREIA-DEV";
}
