using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Rpg.Characters;

namespace Vortex.Revisions.Revision20260701.Parsers.Rpg.Characters;

internal class RpgCharacterChooseClassMessageParser : IParser
{
    public IMessageEvent Parse(IClientPacket packet) =>
        new RpgCharacterChooseClassMessage
        {
            PlayerId = packet.PopInt(),
            ClassId = packet.PopString(),
        };
}
