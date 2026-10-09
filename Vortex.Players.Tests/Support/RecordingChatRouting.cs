using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Chat;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;

namespace Vortex.Players.Tests.Support;

/// <summary>A chat routing that records where each line went (spec F3c §14.1), for the pipeline
/// and channel tests.</summary>
internal sealed class RecordingChatRouting : IChatRouting
{
    public PlayerId Sender { get; init; } = new(1);

    public RoomId? Room { get; set; } = new RoomId(7);

    /// <summary>The players a name finds, case aside.</summary>
    public List<ChatPlayer> Known { get; } = [];

    public HashSet<PlayerId> Online { get; } = [];

    /// <summary>"player &lt;id&gt;" or "&lt;stream&gt;/&lt;key&gt;", and the line.</summary>
    public List<(string To, ChatLine Line)> Deliveries { get; } = [];

    /// <summary>Every delivery throws.</summary>
    public bool Fails { get; set; }

    public Task SendToPlayerAsync(PlayerId to, ChatLine line) =>
        Deliver($"player {to.Value}", line);

    public Task PublishAsync(string stream, long key, ChatLine line) =>
        Deliver($"{stream}/{key}", line);

    public Task<ChatPlayer?> FindPlayerAsync(string name, CancellationToken ct) =>
        Task.FromResult(
            Known.FirstOrDefault(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)
            )
        );

    public Task<bool> IsOnlineAsync(PlayerId player, CancellationToken ct) =>
        Task.FromResult(Online.Contains(player));

    private Task Deliver(string to, ChatLine line)
    {
        if (Fails)
        {
            throw new InvalidOperationException("delivery failed");
        }

        Deliveries.Add((to, line));

        return Task.CompletedTask;
    }
}
