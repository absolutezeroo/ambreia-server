using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Primitives.Chat;

/// <summary>
/// A check a chat message passes before it is routed (spec F3c §14.1): length, rate, later a mute
/// or a word filter. Declared by DI by the module that owns it.
/// </summary>
public interface IChatRule
{
    /// <summary>Lower runs first; equal priorities run by full type name.</summary>
    public int Priority { get; }

    /// <summary>A refusal code, or null to let the message on.</summary>
    public ValueTask<string?> CheckAsync(
        ChatSend send,
        ChatSenderState state,
        CancellationToken ct
    );
}
