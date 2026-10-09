using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Rpg.Emote;

namespace Vortex.Revisions.Revision20260701.Parsers.Rpg.Emote;

internal class RpgEmoteMessageParser : IParser
{
    public IMessageEvent Parse(IClientPacket packet) =>
        new RpgEmoteMessage { Key = packet.PopString() };
}
