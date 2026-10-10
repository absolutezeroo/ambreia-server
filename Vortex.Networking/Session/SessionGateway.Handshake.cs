using System;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Extensions.Logging;
using Vortex.Logging.Extensions;
using Vortex.Primitives.Networking;

namespace Vortex.Networking.Session;

/// <summary>
/// The handshake deadline (audit P0-3): a connection has <see cref="NetworkingConfig.HandshakeTimeout" />
/// to become an account — an accepted SSO ticket — or it is closed. Only the pre-account phase is
/// bounded; a player's silence is the heartbeat's business.
/// </summary>
public sealed partial class SessionGateway
{
    private readonly ConcurrentDictionary<SessionKey, ITimer> _handshakeDeadlines = new();

    private void ArmHandshakeDeadline(SessionKey key)
    {
        TimeSpan timeout = _networkingConfig.HandshakeTimeout;

        if (timeout <= TimeSpan.Zero)
        {
            return;
        }

        ITimer timer = _time.CreateTimer(
            state => OnHandshakeDeadline((SessionKey)state!),
            key,
            timeout,
            Timeout.InfiniteTimeSpan
        );

        // A key admitted twice keeps one timer: the earlier one is dropped with its slot.
        _handshakeDeadlines.AddOrUpdate(
            key,
            timer,
            (_, previous) =>
            {
                previous.Dispose();

                return timer;
            }
        );
    }

    private void DisarmHandshakeDeadline(SessionKey key)
    {
        if (_handshakeDeadlines.TryRemove(key, out ITimer? timer))
        {
            timer.Dispose();
        }
    }

    private void OnHandshakeDeadline(SessionKey key)
    {
        DisarmHandshakeDeadline(key);

        // Bound meanwhile, or already gone: nothing to close.
        if (
            !_sessions.TryGetValue(key, out ISessionContext? ctx)
            || GetAccountId(key) > 0
            || GetPlayerId(key) > 0
        )
        {
            return;
        }

        _logger.LogWarning(
            "Closing session {SessionKey} from {IpAddress}: no ticket within {Timeout}.",
            key,
            ctx.RemoteIpAddress,
            _networkingConfig.HandshakeTimeout
        );

        // The transport's Closed handler does the bookkeeping (RemoveSessionAsync), as for any
        // other close.
        ctx.CloseSessionAsync()
            .LogAndForget(_logger, "Closing a session past its handshake deadline failed.");
    }
}
