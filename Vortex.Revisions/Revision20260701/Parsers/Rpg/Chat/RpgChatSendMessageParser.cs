using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Rpg.Chat;

namespace Vortex.Revisions.Revision20260701.Parsers.Rpg.Chat;

internal class RpgChatSendMessageParser : IParser
{
    public IMessageEvent Parse(IClientPacket packet) =>
        new RpgChatSendMessage
        {
            Channel = packet.PopString(),
            Target = packet.PopString(),
            Text = packet.PopString(),
        };
}
