using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Rpg.Skills;

namespace Vortex.Revisions.Revision20260701.Parsers.Rpg.Skills;

internal class RpgActionBarSetMessageParser : IParser
{
    public IMessageEvent Parse(IClientPacket packet) =>
        new RpgActionBarSetMessage { Slot = packet.PopInt(), ActionId = packet.PopString() };
}
