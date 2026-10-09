using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Chat;

namespace Vortex.Players.Chat.Channels;

/// <summary>
/// <c>whisper</c> (spec F3c §4.4): one player named, wherever they are, and a copy to the sender
/// so the line shows in its conversation tab. Each copy carries the other player's name.
/// </summary>
internal sealed class WhisperChatChannel : IChatChannel
{
    public const string PlayerNotFound = "player_not_found";
    public const string PlayerOffline = "player_offline";

    public string Key => "whisper";

    public bool PlayersCanSend => true;

    public async Task<string?> RouteAsync(
        ChatLine line,
        string target,
        IChatRouting routing,
        CancellationToken ct
    )
    {
        string name = target.Trim();
        ChatPlayer? to = name.Length == 0 ? null : await routing.FindPlayerAsync(name, ct);

        if (to is null || to.Id == routing.Sender)
        {
            return PlayerNotFound;
        }

        if (!await routing.IsOnlineAsync(to.Id, ct))
        {
            return PlayerOffline;
        }

        // Two different presences: told together.
        await Task.WhenAll(
            routing.SendToPlayerAsync(to.Id, line with { Target = line.SenderName }),
            routing.SendToPlayerAsync(routing.Sender, line with { Target = to.Name })
        );

        return null;
    }
}
