using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Preferences;

namespace Vortex.Revisions.Revision20260701.Serializers.Preferences;

internal class AccountPreferencesEventMessageComposerSerializer(int header)
    : AbstractSerializer<AccountPreferencesEventMessageComposer>(header)
{
    protected override void Serialize(
        IServerPacket packet,
        AccountPreferencesEventMessageComposer message
    )
    {
        packet
            .WriteInteger(message.UIVolume)
            .WriteInteger(message.FurniVolume)
            .WriteInteger(message.TraxVolume)
            .WriteBoolean(message.FreeFlowChatDisabled)
            .WriteBoolean(message.RoomInvitesIgnored)
            .WriteBoolean(message.RoomCameraFollowDisabled)
            .WriteInteger((int)message.UIFlags)
            // preferedChatStyle: figé, retiré avec le client -- chat styles were removed (M0 bis).
            .WriteInteger(0)
            .WriteBoolean(message.WiredMenuButton)
            .WriteBoolean(message.WiredInspectButton)
            .WriteBoolean(message.PlayTestMode)
            .WriteInteger(message.VariableSyntaxMode)
            .WriteBoolean(message.WiredWhisperDisabled)
            .WriteBoolean(message.ShowAllNotifications)
            .WriteString(message.UiStyle)
            .WriteInteger(message.ChatSizePreference)
            .WriteInteger((int)message.ChatMode)
            .WriteInteger((int)message.ChatBubbleWidth)
            .WriteInteger((int)message.ChatScrollSpeed);
    }
}
