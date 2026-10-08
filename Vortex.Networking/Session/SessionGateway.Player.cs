using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Observers;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Grains;
using Vortex.Protocol.Messages.Outgoing.Handshake;

namespace Vortex.Networking.Session;

/// <summary>A socket bound to a player: one socket per player, a second one replaces the first
/// with <c>ConcurrentLogin</c>, and a socket that closes while attaching never stays pinned.</summary>
public sealed partial class SessionGateway
{
    public async Task AddSessionToPlayerAsync(
        SessionKey key,
        PlayerId playerId,
        CancellationToken ct = default
    )
    {
        ISessionContextObserver? observer = GetSessionObserver(key);

        if (observer is null)
        {
            return;
        }

        IPlayerPresenceGrain playerPresence = _grainFactory.GetPlayerPresenceGrain(playerId);
        DateTime connectedAt;
        long generation;

        await _mappingGate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            if (!_sessions.ContainsKey(key))
            {
                // The socket closed while the ticket was being checked.
                return;
            }

            // Entering the world from the character screen: the account session lets go of the
            // socket before the player takes it.
            await LeaveAccountPhaseAsync(key, ct).ConfigureAwait(false);

            if (
                _sessionToPlayer.TryGetValue(key, out PlayerId currentPlayerId)
                && currentPlayerId > 0
                && currentPlayerId != playerId
            )
            {
                await RemovePlayerSessionCoreAsync(currentPlayerId, key, closeTransport: false, ct)
                    .ConfigureAwait(false);
            }

            if (
                _playerToSession.TryGetValue(playerId, out SessionKey existingSessionKey)
                && existingSessionKey != key
            )
            {
                await RemovePlayerSessionCoreAsync(
                        playerId,
                        existingSessionKey,
                        closeTransport: true,
                        ct,
                        farewell: new DisconnectReasonEventMessageComposer
                        {
                            Reason = DisconnectReasonEventMessageComposer.ConcurrentLogin,
                        }
                    )
                    .ConfigureAwait(false);
            }

            _sessionToPlayer[key] = playerId;
            _playerToSession[playerId] = key;
            connectedAt = DateTime.UtcNow;
            _playerConnectedAt[playerId] = connectedAt;

            // Claimed here, with the mapping, so the order generations come out in is the order the
            // replacements were decided. The registration itself stays outside the lock -- it is the
            // ordering that had to be established inside it, not the call.
            generation = await playerPresence.BeginSessionAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _mappingGate.Release();
        }

        // Outside the lock, and therefore raceable against a socket that replaces this one before
        // the call lands. The generation is what the presence grain uses to refuse it: a socket that
        // was replaced while it was still attaching would otherwise reinstate itself in front of the
        // live one, and the player would go mute while the server believed they were attached.
        await playerPresence
            .RegisterSessionObserverAsync(observer, generation)
            .ConfigureAwait(false);

        // The socket closed after the mapping was written: its removal may have unregistered the
        // player before the registration above landed, which would leave a dead observer pinned in
        // the presence grain. Take it back out through the normal path.
        if (!_sessions.ContainsKey(key))
        {
            await UndoLateRegistrationAsync(key, playerId, playerPresence).ConfigureAwait(false);

            return;
        }

        await PublishConnectedEventSafelyAsync(playerId, connectedAt).ConfigureAwait(false);
    }

    /// <summary>
    /// The socket closed while it was attaching. If its removal has not run yet, run it now; if it
    /// already ran, its unregister may have landed before this socket's registration, so unregister
    /// again -- unless a newer socket already holds the player.
    /// </summary>
    private async Task UndoLateRegistrationAsync(
        SessionKey key,
        PlayerId playerId,
        IPlayerPresenceGrain playerPresence
    )
    {
        await _mappingGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);

        try
        {
            if (_sessionToPlayer.TryGetValue(key, out PlayerId mapped) && mapped == playerId)
            {
                await RemovePlayerSessionCoreAsync(
                        playerId,
                        key,
                        closeTransport: false,
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);

                return;
            }

            if (_playerToSession.ContainsKey(playerId))
            {
                return;
            }

            await playerPresence
                .UnregisterSessionObserverAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to detach player {PlayerId} from closed session {SessionKey}",
                playerId,
                key
            );
        }
        finally
        {
            _mappingGate.Release();
        }
    }

    public async Task RemoveSessionFromPlayerAsync(PlayerId playerId, CancellationToken ct)
    {
        if (!_playerToSession.TryGetValue(playerId, out SessionKey sessionKey))
        {
            return;
        }

        await _mappingGate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            await RemovePlayerSessionCoreAsync(playerId, sessionKey, closeTransport: true, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            _mappingGate.Release();
        }
    }

    /// <summary>
    ///     Removes the session/player mapping pair and notifies dependents. Callers must hold
    ///     <see cref="_mappingGate" /> before invoking this — it is not reentrant-safe on its own.
    /// </summary>
    private async Task RemovePlayerSessionCoreAsync(
        PlayerId playerId,
        SessionKey sessionKey,
        bool closeTransport,
        CancellationToken ct,
        IComposer? farewell = null
    )
    {
        bool removedActiveMapping = TryRemovePair(_playerToSession, playerId, sessionKey);
        TryRemovePair(_sessionToPlayer, sessionKey, playerId);

        if (!removedActiveMapping)
        {
            return;
        }

        IPlayerPresenceGrain playerPresence = _grainFactory.GetPlayerPresenceGrain(playerId);

        try
        {
            await playerPresence.UnregisterSessionObserverAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to unregister session observer for player {PlayerId}",
                playerId
            );
        }

        if (_playerConnectedAt.TryRemove(playerId, out DateTime connectedAt))
        {
            await PublishDisconnectedEventSafelyAsync(playerId, connectedAt).ConfigureAwait(false);
        }
        else
        {
            _logger.LogWarning(
                "Player {PlayerId} disconnected without a recorded connect timestamp; skipping disconnect duration.",
                playerId
            );
        }

        if (closeTransport)
        {
            await SayFarewellAndCloseAsync(sessionKey, farewell, ct).ConfigureAwait(false);
        }
    }
}
