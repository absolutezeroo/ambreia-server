using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Rpg.Characters;

namespace Vortex.Revisions.Revision20260701.Parsers.Rpg.Characters;

internal class RpgCharacterCreateMessageParser : IParser
{
    public IMessageEvent Parse(IClientPacket packet) =>
        new RpgCharacterCreateMessage
        {
            Name = packet.PopString(),
            Figure = packet.PopString(),
            Gender = packet.PopString(),
        };
}
