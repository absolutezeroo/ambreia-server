using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Chat;
using Vortex.Primitives.Orleans;

namespace Vortex.Players.Chat.Channels;

/// <summary>A channel every player in the world hears (spec F3c §4.4): <c>world</c>, and the
/// <c>trade</c> and <c>recruitment</c> tabs, through the
/// <see cref="OrleansStreamNames.WORLD_CHAT_STREAM"/> stream each presence joins on entry. The
/// client sorts the lines by channel.</summary>
internal sealed class WorldChatChannel(string key) : IChatChannel
{
    public string Key => key;

    public bool PlayersCanSend => true;

    public bool IsDefault => true;

    public async Task<string?> RouteAsync(
        ChatLine line,
        string target,
        IChatRouting routing,
        CancellationToken ct
    )
    {
        await routing.PublishAsync(OrleansStreamNames.WORLD_CHAT_STREAM, 0, line);

        return null;
    }
}
