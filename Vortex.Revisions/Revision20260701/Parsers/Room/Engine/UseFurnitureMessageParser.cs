using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Primitives.Rooms.Object;
using Vortex.Protocol.Messages.Incoming.Room.Engine;

namespace Vortex.Revisions.Revision20260701.Parsers.Room.Engine;

internal class UseFurnitureMessageParser : IParser
{
    public IMessageEvent Parse(IClientPacket packet) =>
        new UseFurnitureMessage
        {
            ObjectId = new RoomObjectId(packet.PopInt()),
            Param = packet.PopInt(),
        };
}
