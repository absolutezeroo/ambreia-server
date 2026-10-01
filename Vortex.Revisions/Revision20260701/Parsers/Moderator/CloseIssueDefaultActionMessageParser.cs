using System.Collections.Immutable;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Moderator;

namespace Vortex.Revisions.Revision20260701.Parsers.Moderator;

internal class CloseIssueDefaultActionMessageParser : IParser
{
    public IMessageEvent Parse(IClientPacket packet)
    {
        int primaryIssueId = packet.PopInt();
        int count = packet.PopCount(sizeof(int));
        ImmutableArray<int>.Builder otherIssueIds = ImmutableArray.CreateBuilder<int>(count);

        for (int i = 0; i < count; i++)
        {
            otherIssueIds.Add(packet.PopInt());
        }

        return new CloseIssueDefaultActionMessage
        {
            PrimaryIssueId = primaryIssueId,
            OtherIssueIds = otherIssueIds.MoveToImmutable(),
            TopicId = packet.PopInt(),
        };
    }
}
