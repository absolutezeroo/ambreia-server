using System.Collections.Immutable;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Moderator;

namespace Vortex.Revisions.Revision20260701.Parsers.Moderator;

internal class CloseIssuesMessageParser : IParser
{
    public IMessageEvent Parse(IClientPacket packet)
    {
        int closeReason = packet.PopInt();
        int count = packet.PopCount(sizeof(int));
        ImmutableArray<int>.Builder issueIds = ImmutableArray.CreateBuilder<int>(count);

        for (int i = 0; i < count; i++)
        {
            issueIds.Add(packet.PopInt());
        }

        return new CloseIssuesMessage
        {
            CloseReason = closeReason,
            IssueIds = issueIds.MoveToImmutable(),
        };
    }
}
