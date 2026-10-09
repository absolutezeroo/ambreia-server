using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Rpg.Items;

namespace Vortex.Revisions.Revision20260701.Parsers.Rpg.Items;

internal class RpgItemDestroyMessageParser : IParser
{
    public IMessageEvent Parse(IClientPacket packet) =>
        new RpgItemDestroyMessage { BagSlot = packet.PopInt(), Quantity = packet.PopInt() };
}
