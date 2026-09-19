using Vortex.Primitives.Moderation;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Callforhelp;

namespace Vortex.Revisions.Revision20260701.Serializers.CallForHelp;

internal class MyCfhReportStatusMessageComposerSerializer(int header)
    : AbstractSerializer<MyCfhReportStatusMessageComposer>(header)
{
    protected override void Serialize(
        IServerPacket packet,
        MyCfhReportStatusMessageComposer message
    )
    {
        packet.WriteInteger(message.Reports.Length);

        foreach (CfhReportStatusSnapshot report in message.Reports)
        {
            packet
                // Read with readLong into an int on the far side, so it is eight bytes here.
                .WriteLong(report.Id)
                .WriteLong(report.CreationTime)
                .WriteString(report.Message)
                .WriteInteger(report.TopicId)
                .WriteString(report.ReportedAccountName)
                .WriteLong(report.CloseTime)
                .WriteBoolean(report.Sanctioned)
                // No auto-moderation in this hotel: every sanction on a report came from a
                // moderator closing it, which is the branch this false selects.
                .WriteBoolean(false)
                .WriteByte((byte)report.AppealStatus)
                // -1, not 0, for the same reason CloseTime uses it on an open report.
                .WriteLong(report.AppealCreationTime)
                .WriteLong(report.AppealResolutionTime);
        }
    }
}
