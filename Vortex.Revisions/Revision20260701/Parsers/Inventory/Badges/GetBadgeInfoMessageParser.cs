using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Inventory.Badges;

namespace Vortex.Revisions.Revision20260701.Parsers.Inventory.Badges;

internal class GetBadgeInfoMessageParser : IParser
{
    public IMessageEvent Parse(IClientPacket packet) =>
        new GetBadgeInfoMessage { BadgeCode = packet.PopString() };
}
