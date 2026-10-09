using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Orleans.Streams;
using Vortex.Players.Chat;
using Vortex.Primitives.Chat;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Snapshots;
using Vortex.Protocol.Messages.Outgoing.Rpg.Chat;

namespace Vortex.Players.Grains;

/// <summary>
/// The player's chat (spec F3c §4, §14): its messages go through <see cref="ChatPipeline"/>, and this
/// grain is the routing the channels deliver by. Its sender state (rate windows) lives here, lost at
/// disconnect. It never calls its room: the zone channel publishes on the room's stream.
/// </summary>
internal sealed partial class PlayerPresenceGrain : IChatRouting
{
    private readonly ChatPipeline _chat;
    private readonly ChatSenderState _chatState = new();
    private StreamSubscriptionHandle<RoomOutbound>? _worldChatSub;

    public async Task SendChatAsync(
        string channel,
        string target,
        string text,
        CancellationToken ct
    )
    {
        // Only a player in the world writes: its socket is attached.
        if (_sessionObserver is null)
        {
            return;
        }

        string? refusal;

        try
        {
            string name = await _grainFactory
                .GetPlayerDirectoryGrain()
                .GetPlayerNameAsync(PlayerId, ct);

            refusal = await _chat.SendAsync(
                new ChatSend(PlayerId, name, channel, target, text, _chat.Time.GetUtcNow()),
                _chatState,
                this,
                ct
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A fault, not a refusal (spec F3c §11): logged, never answered.
            _logger.LogWarning(ex, "The chat message of player {PlayerId} failed.", PlayerId.Value);

            return;
        }

        if (refusal is not null)
        {
            await SendComposerAsync(
                new RpgChatResultMessageComposer { Channel = channel, Code = refusal }
            );
        }
    }

    public Task SendSystemChatAsync(string text, CancellationToken ct) =>
        _chat.SendSystemAsync(text, this, ct);

    PlayerId IChatRouting.Sender => PlayerId;

    RoomId? IChatRouting.Room => _state.ActiveRoomId.Value > 0 ? _state.ActiveRoomId : null;

    Task IChatRouting.SendToPlayerAsync(PlayerId to, ChatLine line)
    {
        RpgChatMessageComposer composer = ToComposer(line);

        // Oneself through the own queue, not through a grain call behind this very turn.
        return to == PlayerId
            ? SendComposerAsync(composer)
            : _grainFactory.GetPlayerPresenceGrain(to).SendComposerAsync(composer);
    }

    Task IChatRouting.PublishAsync(string stream, long key, ChatLine line) =>
        this.GetStreamProvider(OrleansStreamProviders.ROOM_STREAM_PROVIDER)
            .GetStream<RoomOutbound>(StreamId.Create(stream, key))
            .OnNextAsync(
                new RoomOutbound { RoomId = new RoomId((int)key), Composer = ToComposer(line) }
            );

    async Task<ChatPlayer?> IChatRouting.FindPlayerAsync(string name, CancellationToken ct)
    {
        IPlayerDirectoryGrain directory = _grainFactory.GetPlayerDirectoryGrain();

        return await directory.GetPlayerIdByNameAsync(name, ct) is { } id
            ? new ChatPlayer(id, await directory.GetPlayerNameAsync(id, ct))
            : null;
    }

    Task<bool> IChatRouting.IsOnlineAsync(PlayerId player, CancellationToken ct) =>
        player == PlayerId
            ? Task.FromResult(_sessionObserver is not null)
            : _grainFactory.GetPlayerPresenceGrain(player).IsOnlineAsync(ct);

    /// <summary>Listens to the World channel (spec F3c §4), once per activation: a resume or a
    /// second entry does not double it. Its failure costs the World channel only, never the
    /// sign-in. During a grace period it stays: <see cref="OnNextAsync"/> drops what no socket can
    /// take.</summary>
    private async Task JoinWorldChatAsync()
    {
        if (_worldChatSub is not null)
        {
            return;
        }

        try
        {
            _worldChatSub = await this.GetStreamProvider(
                    OrleansStreamProviders.ROOM_STREAM_PROVIDER
                )
                .GetStream<RoomOutbound>(StreamId.Create(OrleansStreamNames.WORLD_CHAT_STREAM, 0L))
                .SubscribeAsync(this);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Player {PlayerId} could not join the world chat.",
                PlayerId.Value
            );
        }
    }

    private async Task LeaveWorldChatAsync()
    {
        if (_worldChatSub is null)
        {
            return;
        }

        try
        {
            await _worldChatSub.UnsubscribeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Player {PlayerId} could not leave the world chat.",
                PlayerId.Value
            );
        }

        _worldChatSub = null;
    }

    private static RpgChatMessageComposer ToComposer(ChatLine line) =>
        new()
        {
            Channel = line.Channel,
            SenderId = line.SenderId.Value,
            SenderName = line.SenderName,
            Text = line.Text,
            SentAt = (int)line.SentAt.ToUnixTimeSeconds(),
            Target = line.Target,
        };
}
