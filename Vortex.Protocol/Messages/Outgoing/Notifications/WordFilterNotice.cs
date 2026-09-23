namespace Vortex.Protocol.Messages.Outgoing.Notifications;

/// <summary>
/// The bubble a player gets when the hotel-wide word filter refuses what they wrote.
/// </summary>
/// <remarks>
/// <c>display=BUBBLE</c> and <c>message</c> travel as parameters, and the client's
/// <c>HabboNotifications.showNotification</c> takes both from the parameters before it looks at
/// external_variables or the texts file — so this needs no client data at all, and the text is the
/// operator's (<c>word_filter.refused_message</c>).
/// </remarks>
public static class WordFilterNotice
{
    public const string Type = "word_filter";

    public static NotificationDialogMessageComposer Create(string message) =>
        new()
        {
            Type = Type,
            Parameters =
            [
                new NotificationDialogParameter { Key = "display", Value = "BUBBLE" },
                new NotificationDialogParameter { Key = "message", Value = message },
            ],
        };
}
