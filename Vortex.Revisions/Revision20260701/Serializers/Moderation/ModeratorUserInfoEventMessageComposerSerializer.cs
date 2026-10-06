using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Moderation;

namespace Vortex.Revisions.Revision20260701.Serializers.Moderation;

internal class ModeratorUserInfoEventMessageComposerSerializer(int header)
    : AbstractSerializer<ModeratorUserInfoEventMessageComposer>(header)
{
    protected override void Serialize(
        IServerPacket packet,
        ModeratorUserInfoEventMessageComposer message
    )
    {
        packet
            .WriteInteger(message.UserId)
            .WriteString(message.UserName)
            .WriteString(message.Figure)
            .WriteInteger(message.RegistrationAgeInMinutes)
            .WriteInteger(message.MinutesSinceLastLogin)
            .WriteBoolean(message.Online)
            .WriteInteger(message.CfhCount)
            .WriteInteger(message.AbusiveCfhCount)
            .WriteInteger(message.CautionCount)
            .WriteInteger(message.BanCount)
            .WriteInteger(message.TradingLockCount)
            .WriteString(message.TradingExpiryDate)
            .WriteString(message.LastPurchaseDate)
            .WriteInteger(message.IdentityId)
            .WriteInteger(message.IdentityRelatedBanCount)
            .WriteString(message.PrimaryEmailAddress)
            // The user classification, frozen: the feature was removed in M0, the field stays.
            .WriteString(string.Empty);

        // Optional tail: the client reads these two only while bytes remain, so they go out as a
        // pair or not at all.
        if (!message.HasSanctionHistory)
        {
            return;
        }

        packet.WriteString(message.LastSanctionTime).WriteInteger(message.SanctionAgeHours);
    }
}
