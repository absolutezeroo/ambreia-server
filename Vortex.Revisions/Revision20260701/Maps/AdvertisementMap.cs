using Vortex.Primitives.Networking.Revisions;
using Vortex.Protocol.Messages.Outgoing.Advertisement;
using Vortex.Revisions.Revision20260701.Parsers.Advertisement;
using Vortex.Revisions.Revision20260701.Serializers.Advertisement;

namespace Vortex.Revisions.Revision20260701.Maps;

internal sealed class AdvertisementMap : IRevisionMap
{
    public void RegisterInto(IRevisionMapBuilder builder)
    {
        builder.MapParser(
            MessageEvent.GetInterstitialMessageEvent,
            new GetInterstitialMessageParser()
        );
        builder.MapParser(
            MessageEvent.InterstitialShownMessageEvent,
            new InterstitialShownMessageParser()
        );

        builder.MapSerializer(
            typeof(InterstitialMessageComposer),
            new InterstitialMessageComposerSerializer(MessageComposer.InterstitialMessageComposer)
        );
    }
}
