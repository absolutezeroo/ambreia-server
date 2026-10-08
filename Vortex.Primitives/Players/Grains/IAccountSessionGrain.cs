using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Orleans.Observers;

namespace Vortex.Primitives.Players.Grains;

/// <summary>
/// The socket of an account on the character screen (spec F3a §5.2), keyed by account id: what
/// <see cref="IPlayerPresenceGrain"/> is for a player, without a room, a queue or a grace period.
/// One socket per account; the gateway closes the one a second connection replaces.
/// </summary>
public interface IAccountSessionGrain : IGrainWithIntegerKey
{
    /// <summary>Claims the next session generation, as <see cref="IPlayerPresenceGrain.BeginSessionAsync"/>.</summary>
    public Task<long> BeginSessionAsync();

    /// <summary>Attaches the socket for <paramref name="generation"/>, or does nothing if a later
    /// one has already claimed the account.</summary>
    public Task RegisterSessionObserverAsync(ISessionContextObserver observer, long generation);

    public Task UnregisterSessionObserverAsync();

    /// <summary>
    /// Straight to the socket, in order; nothing without one. Not interleaved: nothing it calls
    /// comes back to it.
    /// </summary>
    public Task SendComposerAsync(params IComposer[] composers);
}
