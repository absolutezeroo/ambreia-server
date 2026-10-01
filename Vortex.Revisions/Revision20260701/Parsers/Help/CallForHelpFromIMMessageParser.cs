using System.Collections.Immutable;
using Vortex.Primitives.Moderation;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Help;

namespace Vortex.Revisions.Revision20260701.Parsers.Help;

internal class CallForHelpFromIMMessageParser : IParser
{
    public IMessageEvent Parse(IClientPacket packet)
    {
        string message = packet.PopString();
        int topicId = packet.PopInt();
        int reportedUserId = packet.PopInt();

        // The client pushes the flat (userId, text) array and sends length/2 as the count, exactly
        // as the room-based CallForHelp does.
        int evidenceCount = packet.PopCount(sizeof(int) + sizeof(ushort));
        ImmutableArray<CfhEvidenceLine>.Builder evidence =
            ImmutableArray.CreateBuilder<CfhEvidenceLine>(evidenceCount);

        for (int i = 0; i < evidenceCount; i++)
        {
            evidence.Add(new CfhEvidenceLine(packet.PopInt(), packet.PopString()));
        }

        // Trailing pair, after the evidence array (`_SafeCls_3732` pushes p5 and p6 last).
        string reporterName = packet.PopString();
        string reporterEmail = packet.PopString();

        return new CallForHelpFromIMMessage
        {
            Message = message,
            TopicId = topicId,
            ReportedUserId = reportedUserId,
            Evidence = evidence.MoveToImmutable(),
            ReporterName = reporterName,
            ReporterEmail = reporterEmail,
        };
    }
}
