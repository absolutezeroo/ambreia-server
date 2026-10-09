using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Rpg.Interaction;

namespace Vortex.Revisions.Revision20260701.Parsers.Rpg.Interaction;

internal class RpgTargetSelectMessageParser : IParser
{
    public IMessageEvent Parse(IClientPacket packet) =>
        new RpgTargetSelectMessage { ObjectId = packet.PopInt() };
}
