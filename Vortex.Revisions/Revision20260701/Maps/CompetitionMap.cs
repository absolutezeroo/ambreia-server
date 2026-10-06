using Vortex.Primitives.Networking.Revisions;
using Vortex.Protocol.Messages.Outgoing.Competition;
using Vortex.Revisions.Revision20260701.Parsers.Competition;
using Vortex.Revisions.Revision20260701.Serializers.Competition;

namespace Vortex.Revisions.Revision20260701.Maps;

internal sealed class CompetitionMap : IRevisionMap
{
    public void RegisterInto(IRevisionMapBuilder builder)
    {
        builder.MapParser(
            MessageEvent.GetCurrentTimingCodeMessageEvent,
            new GetCurrentTimingCodeMessageParser()
        );

        builder.MapSerializer(
            typeof(CurrentTimingCodeMessageComposer),
            new CurrentTimingCodeMessageComposerSerializer(
                MessageComposer.CurrentTimingCodeMessageComposer
            )
        );
    }
}
