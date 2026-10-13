using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Rpg.Quests;

namespace Vortex.Revisions.Revision20260701.Parsers.Rpg.Quests;

internal class RpgDialogueChooseMessageParser : IParser
{
    public IMessageEvent Parse(IClientPacket packet) =>
        new RpgDialogueChooseMessage { Choice = packet.PopInt() };
}
