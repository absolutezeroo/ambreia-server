using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Chat;
using Vortex.Primitives.Orleans;

namespace Vortex.Players.Chat.Channels;

/// <summary><c>world</c> (spec F3c §4.4): every player in the world, through the
/// <see cref="OrleansStreamNames.WORLD_CHAT_STREAM"/> stream each presence joins on entry.</summary>
internal sealed class WorldChatChannel : IChatChannel
{
    public string Key => "world";

    public bool PlayersCanSend => true;

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
