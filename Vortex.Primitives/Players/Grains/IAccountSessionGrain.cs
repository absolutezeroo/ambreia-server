using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Orleans.Observers;

namespace Vortex.Primitives.Players.Grains;

/// <summary>
/// The socket of an account on the character screen (spec F3a §5.2), keyed by account id: what
/// <see cref="IPlayerPresenceGrain"/> is for a player, without a room or a grace period. One socket
/// per account; the gateway closes the one a second connection replaces.
/// </summary>
public interface IAccountSessionGrain : IGrainWithIntegerKey
{
    /// <summary>
    /// Attaches the socket for <paramref name="generation"/>, or does nothing if a later one has
    /// already claimed the account. Generations come from the gateway, which outlives this
    /// activation: one counted here would start again after a deactivation.
    /// </summary>
    public Task RegisterSessionObserverAsync(ISessionContextObserver observer, long generation);

    public Task UnregisterSessionObserverAsync();

    /// <summary>
    /// Queued and written in order, off the caller's turn; nothing without a socket. Not
    /// interleaved: nothing it calls comes back to it.
    /// </summary>
    public Task SendComposerAsync(params IComposer[] composers);
}
