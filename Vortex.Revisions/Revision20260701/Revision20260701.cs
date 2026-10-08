using Vortex.Primitives.Networking.Revisions;
using Vortex.Revisions.Revision20260701.Maps;

namespace Vortex.Revisions.Revision20260701;

public sealed class Revision20260701()
    : RevisionBase(
        new IRevisionMap[]
        {
            new AvatarMap(),
            new HandshakeMap(),
            new NavigatorMap(),
            new NotificationsMap(),
            new RoomMap(),
            new AvailabilityMap(),
            new RpgCharactersMap(),
        }
    )
{
    public override string Revision => "WIN63-202607011411-782849652";
}
