using Vortex.Primitives.Packets;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Protocol.Messages.Outgoing.Room.Session;

namespace Vortex.Revisions.Revision20260701.Serializers.Room.Session;

internal class CantConnectMessageComposerSerializer(int header)
    : AbstractSerializer<CantConnectMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, CantConnectMessageComposer message)
    {
        packet.WriteInteger((int)message.ErrorType);

        // The string belongs to EnterQueue alone: the client's parser
        // (unknowns/_SafePkg_1744/_SafeCls_2738.parse) reads it only when the reason is 3, and the
        // alert built from it is "${room.queue.error.<parameter>}". Written under Banned it was a
        // trailing field nothing reads; had we ever sent EnterQueue, the client would have read a
        // string that was not there.
        if (message.ErrorType == RoomConnectionErrorType.EnterQueue)
        {
            packet.WriteString(message.AdditionalInfo ?? string.Empty);
        }
    }
}
