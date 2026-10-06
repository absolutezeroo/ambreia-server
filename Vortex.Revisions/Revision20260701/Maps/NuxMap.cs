using Vortex.Primitives.Networking.Revisions;
using Vortex.Protocol.Messages.Outgoing.Nux;
using Vortex.Revisions.Revision20260701.Parsers.Nux;
using Vortex.Revisions.Revision20260701.Serializers.Nux;

namespace Vortex.Revisions.Revision20260701.Maps;

internal sealed class NuxMap : IRevisionMap
{
    public void RegisterInto(IRevisionMapBuilder builder)
    {
        builder.MapParser(
            MessageEvent.SelectInitialRoomEvent,
            new SelectInitialRoomMessageParser()
        );

        builder.MapSerializer(
            typeof(SelectInitialRoomEventMessageComposer),
            new SelectInitialRoomEventMessageComposerSerializer(
                MessageComposer.SelectInitialRoomComposer
            )
        );
    }
}
