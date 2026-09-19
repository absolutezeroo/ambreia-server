using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Camera;

namespace Vortex.Revisions.Revision20260701.Serializers.Camera;

/// <summary>
/// <c>ok</c>, <c>secondsToWait</c>, and the id only when there is one.
/// </summary>
/// <remarks>
/// The client's parser reads the third field behind <c>isOk() &amp;&amp; bytesAvailable</c>, so the
/// omission is part of the contract rather than a shortcut: writing an empty string on a refusal
/// would be read as a share link the player was told they had.
/// </remarks>
internal class CameraPublishStatusMessageComposerSerializer(int header)
    : AbstractSerializer<CameraPublishStatusMessageComposer>(header)
{
    protected override void Serialize(
        IServerPacket packet,
        CameraPublishStatusMessageComposer message
    )
    {
        packet.WriteBoolean(message.Ok).WriteInteger(message.SecondsToWait);

        if (message.Ok && message.ExtraDataId is { Length: > 0 })
        {
            packet.WriteString(message.ExtraDataId);
        }
    }
}
