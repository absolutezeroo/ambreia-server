using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Room.Chat;

namespace Vortex.Revisions.Revision20260701.Parsers.Room.Chat;

internal class ChatMessageParser : IParser
{
    public IMessageEvent Parse(IClientPacket packet)
    {
        string text = packet.PopString();
        packet.PopInt(); // chat style: figé, retiré avec le client (read and ignored)

        return new ChatMessage { Text = text, TrackingId = packet.PopInt() };
    }
}
