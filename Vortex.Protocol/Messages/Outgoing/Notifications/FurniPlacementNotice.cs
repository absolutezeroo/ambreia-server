namespace Vortex.Protocol.Messages.Outgoing.Notifications;

/// <summary>
/// The bubble a player gets when a piece of furniture cannot be placed or moved where they dropped
/// it.
/// </summary>
/// <remarks>
/// The hotel's own <c>external_variables</c> carries <c>notification.furni_placement_error=
/// {"display":"BUBBLE"}</c>, and its texts carry no <c>notification.furni_placement_error.message</c>
/// — so the server names the text, and <c>room.error.cant_set_item</c> ("Sorry, you cannot place
/// this item here.") is the one the texts file holds for it. <c>display</c> travels too, so the
/// bubble draws on a hotel whose variables file lacks the line.
/// </remarks>
public static class FurniPlacementNotice
{
    public const string Type = "furni_placement_error";

    public static NotificationDialogMessageComposer Create() =>
        new()
        {
            Type = Type,
            Parameters =
            [
                new NotificationDialogParameter { Key = "display", Value = "BUBBLE" },
                new NotificationDialogParameter
                {
                    Key = "message",
                    Value = "${room.error.cant_set_item}",
                },
            ],
        };
}
