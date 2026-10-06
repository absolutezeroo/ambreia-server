using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Room.Engine;

namespace Vortex.Revisions.Revision20260701.Parsers.Room.Engine;

internal class MoveAvatarMessageParser : IParser
{
    public IMessageEvent Parse(IClientPacket packet)
    {
        int x = packet.PopInt();
        int y = packet.PopInt();

        // Optional third field, so a sender that predates it still parses: any older client build
        // sends the two-int form. The divergence is deliberate and one-directional: the hotel's own
        // client sends the altitude because it walks in three dimensions, and the clients that do
        // not send it lose nothing.
        int? zKey = packet.Remaining >= sizeof(int) ? packet.PopInt() : null;

        return new MoveAvatarMessage
        {
            TargetX = x,
            TargetY = y,
            TargetZKey = zKey,
        };
    }
}
