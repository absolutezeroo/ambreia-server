using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Primitives.Rooms.Object;
using Vortex.Protocol.Messages.Incoming.Room.Furniture;

namespace Vortex.Revisions.Revision20260701.Parsers.Room.Furniture;

internal class SetAdjacentCustomStackingHeightMessageParser : IParser
{
    public IMessageEvent Parse(IClientPacket packet) =>
        new SetAdjacentCustomStackingHeightMessage
        {
            ObjectId = new RoomObjectId(packet.PopInt()),
            Down = packet.PopBoolean(),
        };
}
