using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Chat;
using Vortex.Primitives.Orleans;

namespace Vortex.Players.Chat.Channels;

/// <summary><c>general</c> (spec F3c §4.4): everyone in the sender's zone room, dungeon copy
/// included, through the stream that room's presences already listen to.</summary>
internal sealed class ZoneChatChannel : IChatChannel
{
    public const string NotInZone = "not_in_zone";

    public string Key => "general";

    public bool PlayersCanSend => true;

    public bool IsDefault => true;

    public async Task<string?> RouteAsync(
        ChatLine line,
        string target,
        IChatRouting routing,
        CancellationToken ct
    )
    {
        if (routing.Room is not { } room)
        {
            return NotInZone;
        }

        await routing.PublishAsync(OrleansStreamNames.ROOM_STREAM, room.Value, line);

        return null;
    }
}
