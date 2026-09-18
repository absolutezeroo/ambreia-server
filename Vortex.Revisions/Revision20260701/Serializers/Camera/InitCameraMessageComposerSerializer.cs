using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Camera;

namespace Vortex.Revisions.Revision20260701.Serializers.Camera;

/// <summary>
/// Field order is the client's: <c>InitCameraMessageParser</c> reads the credit price, then the
/// ducket price, then — only if anything is left — the publish ducket price. All three go out. The
/// guard on the last one means a two-field body would parse cleanly and leave the publish price on
/// the widget's own default, which is the quiet half of what an empty body was already doing.
/// </summary>
internal class InitCameraMessageComposerSerializer(int header)
    : AbstractSerializer<InitCameraMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, InitCameraMessageComposer message)
    {
        packet
            .WriteInteger(message.CreditPrice)
            .WriteInteger(message.DucketPrice)
            .WriteInteger(message.PublishDucketPrice);
    }
}
