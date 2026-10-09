using System.Threading;
using System.Threading.Tasks;
using Orleans.Concurrency;

namespace Vortex.Primitives.Players.Grains;

public partial interface IPlayerPresenceGrain
{
    /// <summary>
    /// A chat message the player typed (spec F3c §4): routed by its channel, or refused with
    /// <c>RpgChatResult</c> to the player alone. Nothing while no socket is attached.
    /// </summary>
    public Task SendChatAsync(string channel, string target, string text, CancellationToken ct);

    /// <summary>A server line to this player in channel <c>system</c> (spec F3c §9); the text may
    /// be a translation key the client translates.</summary>
    /// <remarks>Interleaved like <see cref="SendComposerAsync(Vortex.Primitives.Networking.IComposer)"/>:
    /// it comes from grains this presence may be waiting on (the character during the world entry,
    /// a room during a room entry). The system channel only enqueues on this presence's queue; a
    /// channel replacing it must not await another grain either.</remarks>
    [AlwaysInterleave]
    public Task SendSystemChatAsync(string text, CancellationToken ct);
}
