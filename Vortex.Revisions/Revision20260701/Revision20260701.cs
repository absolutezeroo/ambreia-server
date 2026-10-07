using Microsoft.Extensions.Options;
using Vortex.Primitives.Networking.Revisions;
using Vortex.Revisions.Configuration;
using Vortex.Revisions.Revision20260701.Maps;

namespace Vortex.Revisions.Revision20260701;

public sealed class Revision20260701(IOptions<ProtocolLimitsConfig> protocolLimits)
    : RevisionBase(
        new IRevisionMap[]
        {
            new AvatarMap(),
            new HandshakeMap(),
            new NavigatorMap(),
            new NotificationsMap(),
            new RoomMap(),
            new RoomSettingsMap(protocolLimits.Value),
            new AvailabilityMap(),
        }
    )
{
    public override string Revision => "WIN63-202607011411-782849652";
}
