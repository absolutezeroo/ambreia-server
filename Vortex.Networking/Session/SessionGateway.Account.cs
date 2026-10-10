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

/// <summary>The account phase (spec F3a §5.2): a socket on the character screen, before it is a
/// player. One socket per account; a second one replaces the first.</summary>
public sealed partial class SessionGateway
{
    public async Task AddSessionToAccountAsync(
        SessionKey key,
        int accountId,
        CancellationToken ct = default
    )
    {
        ISessionContextObserver? observer = GetSessionObserver(key);

        if (observer is null || accountId <= 0)
        {
            return;
        }

        // The ticket was accepted: the handshake is done. Before the mapping gate, which can wait
        // behind another login's grain calls — a wait the deadline must not count against this one.
        DisarmHandshakeDeadline(key);

        IAccountSessionGrain accountSession = _grainFactory.GetAccountSessionGrain(accountId);
        long generation;

        await _mappingGate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            if (!_sessions.ContainsKey(key))
            {
                return;
            }

            if (_sessionToPlayer.TryGetValue(key, out PlayerId playerId) && playerId > 0)
            {
                // Already in the world: being an account too would let the phase barrier pass
                // character-screen packets in the world. The player stays.
                _logger.LogWarning(
                    "Ignoring an account ticket for account {AccountId} on session {SessionKey}, "
                        + "already player {PlayerId}",
                    accountId,
                    key,
                    playerId
                );

                return;
            }

            if (_sessionToAccount.TryGetValue(key, out int currentAccountId))
            {
                await RemoveAccountSessionCoreAsync(
                        currentAccountId,
                        key,
                        closeTransport: false,
                        ct
                    )
                    .ConfigureAwait(false);
            }

            if (
                _accountToSession.TryGetValue(accountId, out SessionKey existingSessionKey)
                && existingSessionKey != key
            )
            {
                await RemoveAccountSessionCoreAsync(
                        accountId,
                        existingSessionKey,
                        closeTransport: true,
                        ct
                    )
                    .ConfigureAwait(false);
            }

            _sessionToAccount[key] = accountId;
            _accountToSession[accountId] = key;

            // Claimed with the mapping, so the order of the generations is the order the
            // replacements were decided; counted here because the gateway outlives the grain.
            generation = ++_accountGeneration;
        }
        finally
        {
            _mappingGate.Release();
        }

        await accountSession
            .RegisterSessionObserverAsync(observer, generation)
            .ConfigureAwait(false);

        if (_sessions.ContainsKey(key))
        {
            return;
        }

        // Closed while attaching: its removal may have unregistered before the registration above
        // landed. Unregister again unless a newer socket holds the account.
        await _mappingGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);

        try
        {
            await LeaveAccountPhaseAsync(key, CancellationToken.None).ConfigureAwait(false);

            if (!_accountToSession.ContainsKey(accountId))
            {
                await accountSession.UnregisterSessionObserverAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to detach account {AccountId} from closed session {SessionKey}",
                accountId,
                key
            );
        }
        finally
        {
            _mappingGate.Release();
        }
    }

    /// <summary>Takes a socket out of the account phase, if it is in it. Callers hold
    /// <see cref="_mappingGate"/>.</summary>
    private Task LeaveAccountPhaseAsync(SessionKey key, CancellationToken ct) =>
        _sessionToAccount.TryGetValue(key, out int accountId)
            ? RemoveAccountSessionCoreAsync(accountId, key, closeTransport: false, ct)
            : Task.CompletedTask;

    /// <summary>
    /// Removes the session/account pair and lets go of the account session; with
    /// <paramref name="closeTransport"/>, the socket a second connection replaces is told
    /// <c>ConcurrentLogin</c> and closed. Callers hold <see cref="_mappingGate"/>.
    /// </summary>
    private async Task RemoveAccountSessionCoreAsync(
        int accountId,
        SessionKey sessionKey,
        bool closeTransport,
        CancellationToken ct
    )
    {
        bool removedActiveMapping = TryRemovePair(_accountToSession, accountId, sessionKey);
        TryRemovePair(_sessionToAccount, sessionKey, accountId);

        if (!removedActiveMapping)
        {
            return;
        }

        try
        {
            await _grainFactory
                .GetAccountSessionGrain(accountId)
                .UnregisterSessionObserverAsync()
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to unregister the account session of account {AccountId}",
                accountId
            );
        }

        if (closeTransport)
        {
            await SayFarewellAndCloseAsync(
                    sessionKey,
                    new DisconnectReasonEventMessageComposer
                    {
                        Reason = DisconnectReasonEventMessageComposer.ConcurrentLogin,
                    },
                    ct
                )
                .ConfigureAwait(false);
        }
    }
}
