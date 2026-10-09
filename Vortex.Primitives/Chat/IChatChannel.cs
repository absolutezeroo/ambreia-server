using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Primitives.Chat;

/// <summary>
/// A chat channel (spec F3c §14.1), declared by DI by the module that owns it. The pipeline keeps
/// the last one declared for a key: a module replaces a shipped channel by declaring its own.
/// </summary>
public interface IChatChannel
{
    /// <summary>The key on the wire (<c>general</c>, <c>world</c>…), case-sensitive.</summary>
    public string Key { get; }

    /// <summary>False for a channel only the server writes in (<c>system</c>).</summary>
    public bool PlayersCanSend { get; }

    /// <summary>A channel ready but idle until its system exists (<c>group</c>, <c>guild</c>): any
    /// other channel declared for its key replaces it, whatever the order the modules load in.</summary>
    public bool IsPlaceholder => false;

    /// <summary>Delivers <paramref name="line"/> through <paramref name="routing"/>; a refusal
    /// code, or null once sent. <paramref name="target"/> is what the player named (a whisper's
    /// recipient), empty otherwise.</summary>
    public Task<string?> RouteAsync(
        ChatLine line,
        string target,
        IChatRouting routing,
        CancellationToken ct
    );
}
