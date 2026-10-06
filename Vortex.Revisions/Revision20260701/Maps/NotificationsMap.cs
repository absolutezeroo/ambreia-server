using Vortex.Primitives.Networking.Revisions;
using Vortex.Protocol.Messages.Outgoing.Notifications;
using Vortex.Revisions.Revision20260701.Parsers.Notifications;
using Vortex.Revisions.Revision20260701.Serializers.Notifications;
using Vortex.Revisions.Revision20260701.Serializers.Preferences;

namespace Vortex.Revisions.Revision20260701.Maps;

internal sealed class NotificationsMap : IRevisionMap
{
    public void RegisterInto(IRevisionMapBuilder builder)
    {
        builder.MapParser(
            MessageEvent.ResetUnseenItemIdsEvent,
            new ResetUnseenItemIdsMessageParser()
        );
        builder.MapParser(MessageEvent.ResetUnseenItemsEvent, new ResetUnseenItemsMessageParser());

        builder.MapSerializer(
            typeof(ActivityPointsMessageComposer),
            new ActivityPointsMessageComposerSerializer(
                MessageComposer.ActivityPointsMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(ClubGiftNotificationEventMessageComposer),
            new ClubGiftNotificationEventMessageComposerSerializer(
                MessageComposer.ClubGiftNotificationComposer
            )
        );
        builder.MapSerializer(
            typeof(HabboAchievementNotificationMessageComposer),
            new HabboAchievementNotificationMessageComposerSerializer(
                MessageComposer.HabboAchievementNotificationMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(HabboActivityPointNotificationMessageComposer),
            new HabboActivityPointNotificationMessageComposerSerializer(
                MessageComposer.HabboActivityPointNotificationMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(InfoFeedEnableMessageComposer),
            new InfoFeedEnableMessageComposerSerializer(
                MessageComposer.InfoFeedEnableMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(MOTDNotificationEventMessageComposer),
            new MOTDNotificationEventMessageComposerSerializer(
                MessageComposer.MOTDNotificationComposer
            )
        );
        builder.MapSerializer(
            typeof(NotificationDialogMessageComposer),
            new NotificationDialogMessageComposerSerializer(
                MessageComposer.NotificationDialogMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(PetLevelNotificationEventMessageComposer),
            new PetLevelNotificationEventMessageComposerSerializer(
                MessageComposer.PetLevelNotificationComposer
            )
        );
        builder.MapSerializer(
            typeof(UnseenItemsEventMessageComposer),
            // Same copy-paste as CampaignCalendarDoorOpened had: this was registered against
            // AccountPreferencesEventMessageComposerSerializer, whose cast would throw. Nothing
            // builds an UnseenItemsEventMessageComposer yet, so it never fired.
            new UnseenItemsEventMessageComposerSerializer(MessageComposer.UnseenItemsComposer)
        );
    }
}
