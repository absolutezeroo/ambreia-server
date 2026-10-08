using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Primitives.Events;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Players;

namespace Vortex.Networking.Session;

/// <summary>Saying goodbye to a replaced socket, closing a transport, and the connection lifecycle
/// events -- each guarded so a failure there never undoes the mapping it follows.</summary>
public sealed partial class SessionGateway
{
    private async Task SayFarewellAndCloseAsync(
        SessionKey sessionKey,
        IComposer? farewell,
        CancellationToken ct
    )
    {
        if (!_sessions.TryGetValue(sessionKey, out ISessionContext? session))
        {
            return;
        }

        // Straight to the socket, not through presence: presence now belongs to the session that
        // replaced this one, and must not hear its predecessor's goodbye. Without it the client
        // just loses the connection and shows nothing about why.
        if (farewell is not null)
        {
            try
            {
                await session.SendComposerAsync(farewell, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex,
                    "Failed to send the disconnect reason to replaced session {SessionKey}",
                    sessionKey
                );
            }
        }

        await CloseSessionTransportSafelyAsync(sessionKey, session).ConfigureAwait(false);
    }

    private async Task CloseSessionTransportSafelyAsync(
        SessionKey sessionKey,
        ISessionContext session
    )
    {
        try
        {
            await session.CloseSessionAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to close session transport for {SessionKey}", sessionKey);
        }
    }

    private async Task PublishConnectedEventSafelyAsync(PlayerId playerId, DateTime connectedAt)
    {
        try
        {
            await _events
                .PublishAsync(new PlayerConnectedEvent(playerId, connectedAt))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to publish player connected lifecycle event for {PlayerId}",
                playerId
            );
        }
    }

    private async Task PublishDisconnectedEventSafelyAsync(PlayerId playerId, DateTime connectedAt)
    {
        DateTime disconnectedAt = DateTime.UtcNow;
        TimeSpan duration = disconnectedAt - connectedAt;

        try
        {
            await _events
                .PublishAsync(
                    new PlayerDisconnectedEvent(
                        playerId,
                        connectedAt,
                        disconnectedAt,
                        Math.Max(0, (long)duration.TotalSeconds)
                    )
                )
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to publish player disconnected lifecycle event for {PlayerId}",
                playerId
            );
        }
    }
}
