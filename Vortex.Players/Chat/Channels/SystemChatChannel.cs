using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Chat;

namespace Vortex.Players.Chat.Channels;

/// <summary><c>system</c> (spec F3c §9): a server line to one player; never sent by a player. A
/// module that needs a broadcast replaces it by key.</summary>
internal sealed class SystemChatChannel : IChatChannel
{
    public string Key => ChatLine.SystemChannel;

    public bool PlayersCanSend => false;

    public async Task<string?> RouteAsync(
        ChatLine line,
        string target,
        IChatRouting routing,
        CancellationToken ct
    )
    {
        await routing.SendToPlayerAsync(routing.Sender, line);

        return null;
    }
}
