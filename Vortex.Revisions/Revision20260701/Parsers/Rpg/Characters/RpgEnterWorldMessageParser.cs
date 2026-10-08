using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Rpg.Characters;

namespace Vortex.Revisions.Revision20260701.Parsers.Rpg.Characters;

internal class RpgEnterWorldMessageParser : IParser
{
    public IMessageEvent Parse(IClientPacket packet) =>
        new RpgEnterWorldMessage { PlayerId = packet.PopInt() };
}
