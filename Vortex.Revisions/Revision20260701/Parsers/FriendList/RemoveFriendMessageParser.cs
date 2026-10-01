using System.Collections.Generic;
using System.IO;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.FriendList;

namespace Vortex.Revisions.Revision20260701.Parsers.FriendList;

public class RemoveFriendMessageParser(int maxFriendIds) : IParser
{
    public IMessageEvent Parse(IClientPacket packet)
    {
        int totalFriends = packet.PopCount(sizeof(int));

        if (totalFriends < 0 || totalFriends > maxFriendIds)
        {
            throw new InvalidDataException(
                $"Client declared an invalid friend id count of {totalFriends} (max {maxFriendIds})."
            );
        }

        List<int> friendIds = new List<int>(totalFriends);

        for (int i = 0; i < totalFriends; i++)
        {
            friendIds.Add(packet.PopInt());
        }

        return new RemoveFriendMessage { FriendIds = friendIds };
    }
}
