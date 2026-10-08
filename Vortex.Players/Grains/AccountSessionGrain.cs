using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Logging.Extensions;
using Vortex.Players.Configuration;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Orleans.Observers;
using Vortex.Primitives.Players.Grains;

namespace Vortex.Players.Grains;

/// <inheritdoc cref="IAccountSessionGrain"/>
internal sealed class AccountSessionGrain(
    IOptions<PlayerPresenceConfig> config,
    ILogger<AccountSessionGrain> logger
) : Grain, IAccountSessionGrain
{
    private readonly int _maxQueue = config.Value.MaxOutgoingQueueSize;
    private readonly ILogger<AccountSessionGrain> _logger = logger;
    private readonly Queue<IComposer> _outgoing = new();

    private ISessionContextObserver? _observer;
    private long _generation;
    private bool _draining;

    public Task RegisterSessionObserverAsync(ISessionContextObserver observer, long generation)
    {
        if (generation < _generation)
        {
            // Replaced while it was still attaching: the newer socket keeps the account.
            return Task.CompletedTask;
        }

        _generation = generation;
        _observer = observer;
        _outgoing.Clear();

        // A player can sit on the character screen longer than the collection age without sending
        // anything; a collected activation would forget the socket and answer nobody.
        DelayDeactivation(TimeSpan.MaxValue);

        return Task.CompletedTask;
    }

    public Task UnregisterSessionObserverAsync()
    {
        _observer = null;
        _outgoing.Clear();
        DeactivateOnIdle();

        return Task.CompletedTask;
    }

    public Task SendComposerAsync(params IComposer[] composers)
    {
        if (_observer is null)
        {
            return Task.CompletedTask;
        }

        foreach (IComposer composer in composers)
        {
            if (_outgoing.Count >= _maxQueue)
            {
                // A socket that has stopped reading; it closes on its own, and the character screen
                // is rebuilt by the list the next connection is sent.
                _logger.LogWarning(
                    "Outgoing queue of account {AccountId} is full ({Max}); dropping the rest",
                    this.GetPrimaryKeyLong(),
                    _maxQueue
                );

                break;
            }

            _outgoing.Enqueue(composer);
        }

        // Off the caller's turn: a write that blocks on a full TCP window must not hold the
        // unregister the gateway awaits under its global gate.
        DrainAsync()
            .LogAndForget(
                _logger,
                "Failed to send to the account session of account {AccountId}",
                this.GetPrimaryKeyLong()
            );

        return Task.CompletedTask;
    }

    private async Task DrainAsync()
    {
        if (_draining)
        {
            return;
        }

        _draining = true;

        try
        {
            while (_observer is not null && _outgoing.Count > 0)
            {
                await _observer.SendComposerAsync(_outgoing.Dequeue());
            }
        }
        finally
        {
            _draining = false;
        }
    }
}
