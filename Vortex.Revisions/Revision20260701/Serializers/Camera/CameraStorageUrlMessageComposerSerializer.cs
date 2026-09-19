using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Camera;

namespace Vortex.Revisions.Revision20260701.Serializers.Camera;

/// <summary>
/// One string, which the client reads unconditionally and prefixes with its own
/// <c>stories.image_url_base</c>. Empty is the wire's only way to say "no photo", and the client has
/// a dedicated branch for it.
/// </summary>
internal class CameraStorageUrlMessageComposerSerializer(int header)
    : AbstractSerializer<CameraStorageUrlMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, CameraStorageUrlMessageComposer message)
    {
        packet.WriteString(message.Url);
    }
}
