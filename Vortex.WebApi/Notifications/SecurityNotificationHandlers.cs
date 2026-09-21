using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Notifications;
using Vortex.WebApi.Services;

namespace Vortex.WebApi.Notifications;

/// <summary>
/// What changed on my account.
/// </summary>
/// <remarks>
/// <para>
/// These exist to be read by somebody who did NOT do them. That makes a bell the weaker channel —
/// a thief who owns the session can read and dismiss the notification as easily as the owner, and
/// habbo.com mails this class of thing for exactly that reason. It is still worth writing: the
/// row survives, and the owner coming back later finds a dated record of a change they never made.
/// The mail is the upgrade, not a different feature.
/// </para>
/// <para>
/// The account events carry every avatar on the account, and each one is told separately. A
/// notification belongs to a player, and the player the owner next signs in as is not knowable
/// from here.
/// </para>
/// </remarks>
public sealed class PasswordChangedNotificationHandler(INotificationSink notifications)
    : IEventHandler<AccountPasswordChangedEvent>
{
    public async ValueTask HandleAsync(
        AccountPasswordChangedEvent e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        string data = JsonSerializer.Serialize(new { staffReset = e.StaffReset });

        foreach (int playerId in e.PlayerIds)
        {
            await notifications
                .NotifyAsync(playerId, NotificationKind.PasswordChanged, data: data, ct: ct)
                .ConfigureAwait(false);
        }
    }
}

/// <summary>The second factor was turned on or off.</summary>
public sealed class MfaChangedNotificationHandler(INotificationSink notifications)
    : IEventHandler<AccountMfaChangedEvent>
{
    public async ValueTask HandleAsync(
        AccountMfaChangedEvent e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        string data = JsonSerializer.Serialize(new { enabled = e.Enabled });

        foreach (int playerId in e.PlayerIds)
        {
            await notifications
                .NotifyAsync(playerId, NotificationKind.MfaChanged, data: data, ct: ct)
                .ConfigureAwait(false);
        }
    }
}

/// <summary>
/// My avatar was renamed.
/// </summary>
/// <remarks>
/// The old name is kept in the payload, because it is the only half that identifies the change to
/// the person reading it — they are looking at the new one already.
/// </remarks>
public sealed class NameChangedNotificationHandler(INotificationSink notifications)
    : IEventHandler<PlayerNameChangedEvent>
{
    public async ValueTask HandleAsync(
        PlayerNameChangedEvent e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        await notifications
            .NotifyAsync(
                e.PlayerId,
                NotificationKind.NameChanged,
                data: JsonSerializer.Serialize(new { from = e.OldName, to = e.NewName }),
                ct: ct
            )
            .ConfigureAwait(false);
    }
}
