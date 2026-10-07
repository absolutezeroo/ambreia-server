using Vortex.Primitives.Networking.Revisions;
using Vortex.Protocol.Messages.Outgoing.Navigator;
using Vortex.Revisions.Revision20260701.Serializers.Navigator;

namespace Vortex.Revisions.Revision20260701.Maps;

internal sealed class NavigatorMap : IRevisionMap
{
    public void RegisterInto(IRevisionMapBuilder builder)
    {
        builder.MapSerializer(
            typeof(RoomRatingMessageComposer),
            new RoomRatingMessageComposerSerializer(MessageComposer.RoomRatingComposer)
        );
    }
}
