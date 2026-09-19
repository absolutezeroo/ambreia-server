using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Camera;

namespace Vortex.Revisions.Revision20260701.Serializers.Camera;

/// <summary>
/// <c>ok</c> then the reason, both always. The client reads the string unconditionally, so a success
/// still writes an empty one — a two-field body that stops after the boolean leaves its parser
/// reading the next message's bytes as this one's reason.
/// </summary>
internal class CompetitionStatusMessageComposerSerializer(int header)
    : AbstractSerializer<CompetitionStatusMessageComposer>(header)
{
    protected override void Serialize(
        IServerPacket packet,
        CompetitionStatusMessageComposer message
    )
    {
        packet.WriteBoolean(message.Ok).WriteString(message.ErrorReason);
    }
}
