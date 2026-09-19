using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Users;

namespace Vortex.Revisions.Revision20260701.Parsers.Users;

internal class GetBadgeLeaderboardMessageParser : IParser
{
    public IMessageEvent Parse(IClientPacket packet) =>
        new GetBadgeLeaderboardMessage
        {
            Type = packet.PopInt(),
            Rarity = packet.PopInt(),
            ChunkIndex = packet.PopInt(),
            ChunkSize = packet.PopInt(),
        };
}
