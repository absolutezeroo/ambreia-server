using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Inventory.Furni;

namespace Vortex.Revisions.Revision20260701.Parsers.Inventory.Furni;

internal class RequestRoomPropertySetMessageParser : IParser
{
    public IMessageEvent Parse(IClientPacket packet) =>
        new RequestRoomPropertySetMessage { ItemId = packet.PopInt() };
}
