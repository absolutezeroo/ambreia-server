using System;
using System.Collections.Immutable;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Notifications;

namespace Vortex.Revisions.Revision20260701.Parsers.Notifications;

internal class ResetUnseenItemIdsMessageParser : IParser
{
    private const int BytesPerId = sizeof(int);

    public IMessageEvent Parse(IClientPacket packet)
    {
        int category = packet.PopInt();

        // The count is the client's, so it sizes nothing until the payload backs it up: a peer
        // claiming two billion ids would otherwise reserve the capacity before the first read
        // failed.
        int claimed = packet.PopInt();
        int available = Math.Max(packet.Remaining, 0) / BytesPerId;
        int count = Math.Clamp(claimed, 0, available);

        ImmutableArray<int>.Builder itemIds = ImmutableArray.CreateBuilder<int>(count);

        for (int i = 0; i < count; i++)
        {
            itemIds.Add(packet.PopInt());
        }

        return new ResetUnseenItemIdsMessage
        {
            Category = category,
            ItemIds = itemIds.ToImmutable(),
        };
    }
}
