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
        // sends the two-int form.
        //
        // It is also why incoming/MoveAvatar sits in scripts/hooks/wire-conflicts-baseline.json.
        // Every client the analyzer knows -- the WIN63 AS3, Nitro, Arcturus -- sends two ints, so
        // the check counts three PopInts against two and calls it a disagreement. It cannot see the
        // guard on the line above. The divergence is deliberate and one-directional: the hotel's own
        // client sends the altitude because it walks in three dimensions, and the clients that do
        // not send it lose nothing. Do not "fix" this by dropping the field without dropping the
        // baseline entry with it.
        int? zKey = packet.Remaining >= sizeof(int) ? packet.PopInt() : null;

        return new MoveAvatarMessage
        {
            TargetX = x,
            TargetY = y,
            TargetZKey = zKey,
        };
    }
}
