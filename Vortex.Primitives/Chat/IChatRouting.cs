using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;

namespace Vortex.Primitives.Chat;

/// <summary>
/// What a channel may do to deliver a line (spec F3c §14.1, the « ChatRouting » context): to a
/// player, to everyone on a named stream, find a player by name. Given by the sender's presence; a
/// future channel needs nothing else and never touches the presence.
/// </summary>
public interface IChatRouting
{
    public PlayerId Sender { get; }

    /// <summary>The room the sender stands in, or null.</summary>
    public RoomId? Room { get; }

    public Task SendToPlayerAsync(PlayerId to, ChatLine line);

    /// <summary>To every player listening on stream <paramref name="stream"/> with this key.</summary>
    public Task PublishAsync(string stream, long key, ChatLine line);

    /// <summary>The player of this exact name, case aside, or null.</summary>
    public Task<ChatPlayer?> FindPlayerAsync(string name, CancellationToken ct);

    public Task<bool> IsOnlineAsync(PlayerId player, CancellationToken ct);
}
