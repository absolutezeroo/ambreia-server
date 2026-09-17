using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Orleans.Concurrency;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Orleans.Observers;

namespace Vortex.Primitives.Players.Grains;

public partial interface IPlayerPresenceGrain : IGrainWithIntegerKey
{
    /// <summary>
    /// Claims the next session generation for this player, and answers it.
    /// </summary>
    /// <remarks>
    /// Called while the gateway still holds the decision that this socket replaces the previous one,
    /// so the order generations are handed out in is the order the replacements were decided.
    /// Orleans serialises calls to this activation, which is what makes "next" mean anything.
    /// </remarks>
    public Task<long> BeginSessionAsync(CancellationToken ct);

    /// <summary>
    /// Attaches the socket for <paramref name="generation"/>, or does nothing if a later one has
    /// already claimed the player.
    /// </summary>
    /// <remarks>
    /// The registration used to happen after the gateway released its lock and carried nothing that
    /// said which session it was for. An interleaving where the old socket was slow to register --
    /// replaced, then arriving afterwards -- reinstated a socket nobody was listening to, and the
    /// player went mute with the server believing they were attached. The generation is what makes
    /// a late arrival harmless instead of destructive.
    /// </remarks>
    public Task RegisterSessionObserverAsync(ISessionContextObserver observer, long generation);
    public Task UnregisterSessionObserverAsync(CancellationToken ct);
    public Task<bool> IsOnlineAsync(CancellationToken ct);

    // Interleaved on purpose. Rooms push composers at a player from inside calls that the player's own
    // presence grain made (entering/leaving a room), so a non-interleaved send would sit behind that
    // in-flight request forever: the presence grain is blocked awaiting the room, the room is blocked
    // awaiting the presence grain, and both only unwedge on the 30s Orleans call timeout. The handlers
    // are safe to interleave because they only enqueue onto the outgoing queue synchronously — no
    // awaits, so no other turn can observe a half-applied state change.
    [AlwaysInterleave]
    public Task SendComposerAsync(IComposer composer);

    [AlwaysInterleave]
    public Task SendComposerAsync(params IComposer[] composers);
}
