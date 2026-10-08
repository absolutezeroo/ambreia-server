using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Orleans.Observers;
using Vortex.Primitives.Players.Grains;

namespace Vortex.Players.Grains;

/// <inheritdoc cref="IAccountSessionGrain"/>
internal sealed class AccountSessionGrain(ILogger<AccountSessionGrain> logger)
    : Grain,
        IAccountSessionGrain
{
    private readonly ILogger<AccountSessionGrain> _logger = logger;

    private ISessionContextObserver? _observer;
    private long _generation;

    public Task<long> BeginSessionAsync() => Task.FromResult(++_generation);

    public Task RegisterSessionObserverAsync(ISessionContextObserver observer, long generation)
    {
        if (generation < _generation)
        {
            // Replaced while it was still attaching: the newer socket keeps the account.
            return Task.CompletedTask;
        }

        _observer = observer;

        // A player can sit on the character screen longer than the collection age without sending
        // anything; a collected activation would forget the socket and answer nobody.
        DelayDeactivation(TimeSpan.MaxValue);

        return Task.CompletedTask;
    }

    public Task UnregisterSessionObserverAsync()
    {
        _observer = null;
        DeactivateOnIdle();

        return Task.CompletedTask;
    }

    public async Task SendComposerAsync(params IComposer[] composers)
    {
        if (_observer is null)
        {
            return;
        }

        try
        {
            foreach (IComposer composer in composers)
            {
                await _observer.SendComposerAsync(composer);
            }
        }
        catch (Exception ex)
        {
            // The socket is going; its close unregisters it. The request that led here already
            // succeeded or failed on its own, and must not fail again because nobody heard it.
            _logger.LogWarning(
                ex,
                "Failed to send to the account session of account {AccountId}",
                this.GetPrimaryKeyLong()
            );
        }
    }
}
