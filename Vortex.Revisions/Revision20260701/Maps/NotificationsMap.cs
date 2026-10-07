using Vortex.Primitives.Networking.Revisions;
using Vortex.Protocol.Messages.Outgoing.Notifications;
using Vortex.Revisions.Revision20260701.Serializers.Notifications;

namespace Vortex.Revisions.Revision20260701.Maps;

internal sealed class NotificationsMap : IRevisionMap
{
    public void RegisterInto(IRevisionMapBuilder builder)
    {
        builder.MapSerializer(
            typeof(NotificationDialogMessageComposer),
            new NotificationDialogMessageComposerSerializer(
                MessageComposer.NotificationDialogMessageComposer
            )
        );
    }
}
