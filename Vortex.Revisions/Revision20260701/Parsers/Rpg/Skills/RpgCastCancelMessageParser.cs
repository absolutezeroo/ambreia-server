using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Rpg.Skills;

namespace Vortex.Revisions.Revision20260701.Parsers.Rpg.Skills;

internal class RpgCastCancelMessageParser : IParser
{
    public IMessageEvent Parse(IClientPacket packet) => new RpgCastCancelMessage();
}
