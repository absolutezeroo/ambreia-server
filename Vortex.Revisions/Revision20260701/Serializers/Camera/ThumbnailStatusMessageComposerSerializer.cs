using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Camera;

namespace Vortex.Revisions.Revision20260701.Serializers.Camera;

internal class ThumbnailStatusMessageComposerSerializer(int header)
    : AbstractSerializer<ThumbnailStatusMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, ThumbnailStatusMessageComposer message)
    {
        packet.WriteBoolean(message.Ok).WriteBoolean(message.RenderLimitHit);
    }
}
