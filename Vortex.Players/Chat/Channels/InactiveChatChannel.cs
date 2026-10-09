using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Chat;

namespace Vortex.Players.Chat.Channels;

/// <summary>A channel ready but idle until its system exists (spec F3c §2): every message is
/// refused with <paramref name="code"/>. The group module (M4) and the guild one replace theirs by
/// declaring a channel of the same key.</summary>
internal sealed class InactiveChatChannel(string key, string code) : IChatChannel
{
    public string Key => key;

    public bool PlayersCanSend => true;

    public bool IsDefault => true;

    public Task<string?> RouteAsync(
        ChatLine line,
        string target,
        IChatRouting routing,
        CancellationToken ct
    ) => Task.FromResult<string?>(code);
}
