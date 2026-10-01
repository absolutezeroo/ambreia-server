using System.Collections.Generic;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.FriendList;

namespace Vortex.Revisions.Revision20260701.Parsers.FriendList;

public class AcceptFriendMessageParser : IParser
{
    public IMessageEvent Parse(IClientPacket packet)
    {
        int friendsCount = packet.PopCount(sizeof(int));

        List<int> friends = new List<int>(friendsCount);

        for (int i = 0; i < friendsCount; i++)
        {
            int userId = packet.PopInt();

            friends.Add(userId);
        }

        return new AcceptFriendMessage { Friends = friends };
    }
}
