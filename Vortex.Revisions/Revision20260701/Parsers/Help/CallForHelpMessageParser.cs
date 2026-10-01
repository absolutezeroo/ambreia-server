using System.Collections.Immutable;
using Vortex.Primitives.Moderation;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Help;

namespace Vortex.Revisions.Revision20260701.Parsers.Help;

internal class CallForHelpMessageParser : IParser
{
    public IMessageEvent Parse(IClientPacket packet)
    {
        string message = packet.PopString();
        int topicId = packet.PopInt();
        int reportedUserId = packet.PopInt();
        int roomId = packet.PopInt();

        int evidenceCount = packet.PopCount(sizeof(int) + sizeof(ushort));
        ImmutableArray<CfhEvidenceLine>.Builder evidence =
            ImmutableArray.CreateBuilder<CfhEvidenceLine>(evidenceCount);

        for (int i = 0; i < evidenceCount; i++)
        {
            evidence.Add(new CfhEvidenceLine(packet.PopInt(), packet.PopString()));
        }

        string reporterName = packet.PopString();
        string reporterEmail = packet.PopString();

        return new CallForHelpMessage
        {
            Message = message,
            TopicId = topicId,
            ReportedUserId = reportedUserId,
            RoomId = roomId,
            Evidence = evidence.MoveToImmutable(),
            ReporterName = reporterName,
            ReporterEmail = reporterEmail,
        };
    }
}
