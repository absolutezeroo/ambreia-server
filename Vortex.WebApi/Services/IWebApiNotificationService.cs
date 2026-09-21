using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Notifications;

namespace Vortex.WebApi.Services;

/// <summary>
/// Reading back what a player has been told. The writing half is <see cref="INotificationSink"/>,
/// filled by the event handlers under <c>Notifications/</c>.
/// </summary>
public interface IWebApiNotificationService
{
    /// <summary>My notifications, newest first, with the unread count for the whole list.</summary>
    Task<NotificationPage> GetAsync(
        int playerId,
        int page,
        int pageSize,
        CancellationToken ct = default
    );

    /// <summary>
    /// Marks every unread notification read. Returns how many moved.
    /// </summary>
    /// <remarks>
    /// All of them, not the ones on screen. The counter is what a player is clearing, and leaving
    /// an older page unread would make it come straight back — a bell that will not go out is
    /// worse than one that is occasionally too eager.
    /// </remarks>
    Task<int> MarkSeenAsync(int playerId, CancellationToken ct = default);
}

/// <summary>One line, as the website reads it.</summary>
/// <param name="Actor">
/// Who caused it, resolved to a name and a figure so the page can draw them. Null when the hotel
/// itself did it — a lapsed membership, a room raid — and also when the player has since been
/// removed, which the site has to render without.
/// </param>
/// <param name="Data">
/// The kind's own payload, passed through as raw JSON. Untyped on purpose: thirty kinds carry
/// thirty shapes, and the alternative is thirty records of which the site uses one.
/// </param>
public sealed record Notification(
    int Id,
    NotificationKind Kind,
    DateTime CreatedAt,
    bool Seen,
    NotificationActor? Actor,
    int? TargetId,
    string? Data
);

/// <summary>The player behind a notification, as much of them as a line needs.</summary>
public sealed record NotificationActor(int Id, string Name, string FigureString);

public sealed record NotificationPage(
    int Page,
    int PageSize,
    int Total,
    int Unread,
    IReadOnlyList<Notification> Items
);
