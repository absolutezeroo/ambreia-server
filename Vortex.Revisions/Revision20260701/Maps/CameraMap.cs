using Vortex.Primitives.Networking.Revisions;
using Vortex.Protocol.Messages.Outgoing.Camera;
using Vortex.Revisions.Revision20260701.Parsers.Camera;
using Vortex.Revisions.Revision20260701.Serializers.Camera;

namespace Vortex.Revisions.Revision20260701.Maps;

internal sealed class CameraMap : IRevisionMap
{
    public void RegisterInto(IRevisionMapBuilder builder)
    {
        builder.MapParser(
            MessageEvent.PhotoCompetitionMessageEvent,
            new PhotoCompetitionMessageParser()
        );
        builder.MapParser(MessageEvent.PublishPhotoMessageEvent, new PublishPhotoMessageParser());
        builder.MapParser(MessageEvent.PurchasePhotoMessageEvent, new PurchasePhotoMessageParser());
        builder.MapParser(MessageEvent.RenderRoomMessageEvent, new RenderRoomMessageParser());
        builder.MapParser(
            MessageEvent.RenderRoomThumbnailMessageEvent,
            new RenderRoomThumbnailMessageParser()
        );
        builder.MapParser(
            MessageEvent.RequestCameraConfigurationMessageEvent,
            new RequestCameraConfigurationMessageParser()
        );

        builder.MapSerializer(
            typeof(CameraPublishStatusMessageComposer),
            new CameraPublishStatusMessageComposerSerializer(
                MessageComposer.CameraPublishStatusMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(CameraPurchaseOKMessageComposer),
            new CameraPurchaseOKMessageComposerSerializer(
                MessageComposer.CameraPurchaseOKMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(CameraStorageUrlMessageComposer),
            new CameraStorageUrlMessageComposerSerializer(
                MessageComposer.CameraStorageUrlMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(CompetitionStatusMessageComposer),
            new CompetitionStatusMessageComposerSerializer(
                MessageComposer.CompetitionStatusMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(InitCameraMessageComposer),
            new InitCameraMessageComposerSerializer(MessageComposer.InitCameraMessageComposer)
        );
        builder.MapSerializer(
            typeof(ThumbnailStatusMessageComposer),
            new ThumbnailStatusMessageComposerSerializer(
                MessageComposer.ThumbnailStatusMessageComposer
            )
        );
    }
}
