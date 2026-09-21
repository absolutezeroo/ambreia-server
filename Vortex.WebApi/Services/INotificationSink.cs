using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Notifications;

namespace Vortex.WebApi.Services;

/// <summary>
/// The one way a notification is written. Every handler under <c>Notifications/</c> calls this and
/// nothing else, so what a player is told is decided in one place rather than in each domain.
/// </summary>
/// <remarks>
/// <para>
/// Here and not in <c>Vortex.Primitives</c>: notifications are the website's, and the producers
/// are the handlers in this project. Nothing outside raises one, so the contract does not need to
/// be reachable from outside — and putting it in Primitives would advertise a plugin surface that
/// does not exist.
/// </para>
/// <para>
/// Nothing here is about delivery. A row is written and the player reads it when they next open
/// the site — which is the whole point, since the cases worth notifying (a membership that lapsed,
/// someone who blocked you) are precisely the ones that happen while nobody is looking.
/// </para>
/// </remarks>
public interface INotificationSink
{
    /// <summary>
    /// Tells <paramref name="playerId"/> that <paramref name="kind"/> happened.
    /// </summary>
    /// <param name="actorPlayerId">Who caused it, when that is a player and not the hotel.</param>
    /// <param name="targetId">
    /// What it points at, read together with <paramref name="kind"/> — a feed post id, a room id,
    /// a group id. Deliberately untyped: every kind knows what its own target is, and a column per
    /// domain would be thirty nullable columns of which one is ever set.
    /// </param>
    /// <param name="data">
    /// The few values the website needs to word the line — a credit amount, a badge code, a room
    /// name — as JSON. Kept small and denormalised on purpose: a notification has to still read
    /// correctly after the thing it is about has been deleted or renamed.
    /// </param>
    Task NotifyAsync(
        int playerId,
        NotificationKind kind,
        int? actorPlayerId = null,
        int? targetId = null,
        string? data = null,
        CancellationToken ct = default
    );
}
