using Vortex.Primitives.Networking.Revisions;
using Vortex.Protocol.Messages.Outgoing.Availability;
using Vortex.Revisions.Revision20260701.Serializers.Availability;

namespace Vortex.Revisions.Revision20260701.Maps;

internal sealed class AvailabilityMap : IRevisionMap
{
    public void RegisterInto(IRevisionMapBuilder builder)
    {
        builder.MapSerializer(
            typeof(MaintenanceStatusMessageComposer),
            new MaintenanceStatusMessageComposerSerializer(
                MessageComposer.MaintenanceStatusMessageComposer
            )
        );
    }
}
