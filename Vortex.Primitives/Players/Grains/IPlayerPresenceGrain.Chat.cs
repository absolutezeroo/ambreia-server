using System.Threading;
using System.Threading.Tasks;

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
    public Task SendSystemChatAsync(string text, CancellationToken ct);
}
