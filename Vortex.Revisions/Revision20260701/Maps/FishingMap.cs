using Vortex.Primitives.Networking.Revisions;
using Vortex.Protocol.Messages.Incoming.Fishing;
using Vortex.Protocol.Messages.Outgoing.Fishing;
using Vortex.Revisions.Revision20260701.Parsers.Fishing;
using Vortex.Revisions.Revision20260701.Serializers.Fishing;

namespace Vortex.Revisions.Revision20260701.Maps;

/// <summary>
/// The fishing system's wire registrations. Vortex-specific: no AS3 or Habbo equivalent, so these
/// ids appear in no client registry and are allocated from the 8000-8999 band — see Headers.cs.
/// </summary>
/// <remarks>
/// Five parsers against ten serializers, which is the shape of the feature: the client starts a
/// session, stops it, mounts a catch and plays one minigame. Everything else — which
/// fish, what weight, what rewards, when the spot runs dry — is decided here and arrives unasked.
/// </remarks>
internal sealed class FishingMap : IRevisionMap
{
    public void RegisterInto(IRevisionMapBuilder builder)
    {
        builder.MapParser(
            MessageEvent.VortexStartFishingMessageEvent,
            new VortexStartFishingMessageParser()
        );
        builder.MapParser(
            MessageEvent.VortexStopFishingMessageEvent,
            new VortexStopFishingMessageParser()
        );
        builder.MapParser(
            MessageEvent.VortexFishingMountCatchMessageEvent,
            new VortexFishingMountCatchMessageParser()
        );
        builder.MapParser(
            MessageEvent.VortexHookHavocInputMessageEvent,
            new VortexHookHavocInputMessageParser()
        );

        builder.MapSerializer(
            typeof(VortexFishingDefinitionsMessageComposer),
            new VortexFishingDefinitionsMessageComposerSerializer(
                MessageComposer.VortexFishingDefinitionsMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(VortexFishingPlayerStateMessageComposer),
            new VortexFishingPlayerStateMessageComposerSerializer(
                MessageComposer.VortexFishingPlayerStateMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(VortexFishSightedMessageComposer),
            new VortexFishSightedMessageComposerSerializer(
                MessageComposer.VortexFishSightedMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(VortexFishingCatchResultMessageComposer),
            new VortexFishingCatchResultMessageComposerSerializer(
                MessageComposer.VortexFishingCatchResultMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(VortexFishingSpotDepletedMessageComposer),
            new VortexFishingSpotDepletedMessageComposerSerializer(
                MessageComposer.VortexFishingSpotDepletedMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(VortexFishingErrorMessageComposer),
            new VortexFishingErrorMessageComposerSerializer(
                MessageComposer.VortexFishingErrorMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(VortexFishingRecordsMessageComposer),
            new VortexFishingRecordsMessageComposerSerializer(
                MessageComposer.VortexFishingRecordsMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(VortexHookHavocStartedMessageComposer),
            new VortexHookHavocStartedMessageComposerSerializer(
                MessageComposer.VortexHookHavocStartedMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(VortexHookHavocResultMessageComposer),
            new VortexHookHavocResultMessageComposerSerializer(
                MessageComposer.VortexHookHavocResultMessageComposer
            )
        );
    }
}
