using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Users;

namespace Vortex.Revisions.Revision20260701.Serializers.Users;

internal class ScrSendUserInfoMessageSerializer(int header)
    : AbstractSerializer<ScrSendUserInfoMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, ScrSendUserInfoMessageComposer message)
    {
        packet
            .WriteString(message.ProductName)
            .WriteInteger(message.DaysToPeriodEnd)
            .WriteInteger(message.MemberPeriods)
            .WriteInteger(message.PeriodsSubscribedAhead)
            .WriteInteger(message.ResponseType)
            .WriteBoolean(message.HasEverBeenMember)
            .WriteBoolean(message.IsVIP)
            .WriteInteger(message.PastMembershipDays)
            .WriteInteger(message.PastVipDays)
            .WriteInteger(message.MinutesUntilExpiration)
            .WriteInteger(message.MinutesSinceLastModified);
    }
}
