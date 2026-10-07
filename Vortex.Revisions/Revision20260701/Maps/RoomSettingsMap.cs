using Vortex.Primitives.Networking.Revisions;
using Vortex.Revisions.Configuration;

namespace Vortex.Revisions.Revision20260701.Maps;

internal sealed class RoomSettingsMap : IRevisionMap
{
    private readonly ProtocolLimitsConfig _protocolLimits;

    public RoomSettingsMap(ProtocolLimitsConfig protocolLimits)
    {
        _protocolLimits = protocolLimits;
    }

    public void RegisterInto(IRevisionMapBuilder builder)
    {
        // This map registered parsers only, so every room-settings composer the server built was
        // dropped for want of a serializer — the settings dialog never received a single byte.
        // The remaining composers in this domain are still empty-bodied stubs; registering those
        // would put truncated packets on the wire, so they stay unmapped until they are written.
    }
}
