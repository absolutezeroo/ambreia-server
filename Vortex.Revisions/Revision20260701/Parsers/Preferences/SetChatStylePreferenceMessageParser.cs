using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Preferences;

namespace Vortex.Revisions.Revision20260701.Parsers.Preferences;

internal class SetChatStylePreferenceMessageParser : IParser
{
    public IMessageEvent Parse(IClientPacket packet)
    {
        packet.PopInt(); // chat style: figé, retiré avec le client (read and ignored)

        return new SetChatStylePreferenceMessage { FontSizeMode = packet.PopInt() };
    }
}
