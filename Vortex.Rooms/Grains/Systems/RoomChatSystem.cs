using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Database.Entities.Room;
using Vortex.Events.Registry;
using Vortex.Primitives.Action;
using Vortex.Primitives.Events;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Navigator.Enums;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Pets.Snapshots;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Events.Player;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Protocol.Messages.Outgoing.Habbicons;
using Vortex.Protocol.Messages.Outgoing.Notifications;
using Vortex.Protocol.Messages.Outgoing.Room.Chat;
using Vortex.Rooms.Object.Avatars.Player;

namespace Vortex.Rooms.Grains.Systems;

public sealed class RoomChatSystem(RoomGrain roomGrain)
{
    private readonly RoomGrain _roomGrain = roomGrain;
    private static readonly int MaxChatMessageLength = 100;

    // Per-player burst state for the room's configured flood sensitivity (SEC-03): when the player
    // last spoke, and how many lines they have already spent out of their allowance. Lives only for
    // this room activation; bounded by how many distinct players have spoken since it activated.
    private readonly Dictionary<PlayerId, (long LastMs, int Burst)> _floodStateByPlayer = new();

    public async Task SendChatFromPlayerAsync(
        PlayerId playerId,
        string text,
        AvatarGestureType gesture,
        int styleId,
        List<(string, string, bool)> links,
        int trackingId,
        PlayerId? targetPlayerId = null,
        RoomChatType chatType = RoomChatType.Chat
    )
    {
        if (
            targetPlayerId is not null
            && !_roomGrain._state.AvatarsByPlayerId.ContainsKey(targetPlayerId.Value)
        )
        {
            return;
        }

        if (
            !_roomGrain._state.AvatarsByPlayerId.TryGetValue(playerId, out RoomObjectId objectId)
            || !_roomGrain._state.AvatarsByObjectId.TryGetValue(objectId, out IRoomAvatar? avatar)
        )
        {
            return;
        }

        // The room's own silence switch. Nothing is sent back: the client has already drawn the
        // line locally and there is no "the room is muted" packet, exactly as the reference
        // emulator drops it (RoomChatManager:295). Rights-holders keep talking.
        if (
            _roomGrain._state.AllInRoomMuted
            && !_roomGrain.SecurityModule.HasExplicitRights(playerId)
        )
        {
            return;
        }

        if (IsUserMuted(playerId, out int secondsRemaining))
        {
            await _roomGrain
                ._grainFactory.GetPlayerPresenceGrain(playerId)
                .SendComposerAsync(
                    new RemainingMutePeriodMessageComposer { SecondsRemaining = secondsRemaining }
                );

            return;
        }

        if (IsFloodGated(playerId, avatar, out int floodSecondsRemaining))
        {
            await _roomGrain
                ._grainFactory.GetPlayerPresenceGrain(playerId)
                .SendComposerAsync(
                    new FloodControlMessageComposer { Seconds = floodSecondsRemaining }
                );

            return;
        }

        // After mute and flood control, before anyone sees the line: those two already refuse with a
        // composer of their own, and a line dropped here is dropped for everybody including the
        // speaker, so it must not have been sent yet.
        EventContext chatting = await _roomGrain._cancellableEvents.PublishCancellableAsync(
            new PlayerChattingEvent(playerId, _roomGrain.RoomId.Value, text, targetPlayerId),
            CancellationToken.None
        );

        if (chatting.Cancel)
        {
            return;
        }

        if (styleId == -1)
        {
            styleId = avatar.LastChatStyleId;
        }
        else
        {
            avatar.LastChatStyleId = styleId;
        }

        // After the cancellable event, so a handler still sees what was actually typed, and before
        // the line reaches anybody -- including the speaker, who must see the same masked text as
        // the room or they would not know the filter exists. The hotel's list first, then the room
        // owner's; a refusal by the hotel's ends the line here, for everybody.
        string? allowed = await ApplyHotelFilterAsync(playerId, text).ConfigureAwait(true);

        if (allowed is null)
        {
            return;
        }

        text = ApplyFilterWords(allowed);

        await SendChatAsync(
            avatar.ObjectId,
            playerId,
            text,
            gesture,
            styleId,
            links,
            trackingId,
            targetPlayerId,
            chatType
        );

        // Said, and seen. PlayerChattingEvent above is a *pre* event a handler can cancel, so it
        // is not evidence anybody heard the line; progression hangs off this one instead, which is
        // raised only once the room has actually sent it.
        await _roomGrain._events.PublishAsync(
            new PlayerChattedEvent(
                playerId,
                _roomGrain.RoomId.Value,
                targetPlayerId is not null,
                text
            ),
            CancellationToken.None
        );

        // A public line may also be an order to one of the speaker's pets.
        if (targetPlayerId is null)
        {
            await TryIssuePetCommandFromChatAsync(playerId, text);
        }

        // Public chat (not whispers) feeds the wired "avatar says (keyword)" trigger.
        if (targetPlayerId is null && !string.IsNullOrWhiteSpace(text))
        {
            await _roomGrain.PublishRoomEventAsync(
                new PlayerChatEvent
                {
                    RoomId = _roomGrain.RoomId,
                    CausedBy = ActionContext.CreateForPlayer(playerId, _roomGrain.RoomId),
                    PlayerId = playerId,
                    Message = text,
                },
                CancellationToken.None
            );
        }
    }

    /// <summary>
    /// Recognises "&lt;pet name&gt; &lt;command&gt;" in a public line and runs the command.
    ///
    /// There is no "issue pet command" packet in the client: AS3's PetCommandTool builds the string
    /// "&lt;pet name&gt; &lt;localised command&gt;" and hands it to roomSession.sendChatMessage(), so
    /// an order and a chat line are the same message on the wire. That is also why a player can type
    /// the order by hand. Only the pet's own owner is obeyed, which IssueCommandAsync enforces again.
    /// </summary>
    private async Task TryIssuePetCommandFromChatAsync(PlayerId playerId, string text)
    {
        if (string.IsNullOrWhiteSpace(text) || _roomGrain._state.PetsById.Count == 0)
        {
            return;
        }

        string trimmed = text.Trim();
        int split = trimmed.IndexOf(' ');

        // A bare name with no command word is just chat.
        if (split <= 0)
        {
            return;
        }

        string spokenName = trimmed[..split];
        string spokenCommand = trimmed[(split + 1)..].Trim();

        if (spokenCommand.Length == 0)
        {
            return;
        }

        foreach (PetSnapshot pet in _roomGrain._state.PetsById.Values)
        {
            if (pet.OwnerId != playerId)
            {
                continue;
            }

            if (!string.Equals(pet.Name, spokenName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int? commandId = _roomGrain._petCommandProvider.ResolveCommandIdByName(
                pet.Type,
                spokenCommand
            );

            if (commandId is null)
            {
                return;
            }

            await _roomGrain.PetSystem.IssueCommandAsync(
                ActionContext.CreateForPlayer(playerId, _roomGrain.RoomId),
                pet.PetId,
                commandId.Value,
                CancellationToken.None
            );

            return;
        }
    }

    /// <summary>
    /// Shows a Habbicon over a player's avatar for everyone in the room.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A Habbicon in a room is a communication act, not a decoration, so it lives here and goes
    /// through the gate a line of chat goes through: the player has to be in the room, not muted,
    /// and not flooding. That is what stops the selector from being a mute bypass, and it is the
    /// reason this is not a system of its own — a second gate could disagree with this one.
    /// </para>
    /// <para>
    /// Ownership is deliberately not checked here. The room knows who is standing in it and nothing
    /// about what they own; the Habbicon grain establishes that before calling, which keeps an
    /// inventory query out of the room's turn.
    /// </para>
    /// </remarks>
    /// <returns>False when the room refused it, so the caller does not record a use nobody saw.</returns>
    public async Task<bool> UseHabbiconAsync(PlayerId playerId, int habbiconId)
    {
        if (
            !_roomGrain._state.AvatarsByPlayerId.TryGetValue(playerId, out RoomObjectId objectId)
            || !_roomGrain._state.AvatarsByObjectId.TryGetValue(objectId, out IRoomAvatar? avatar)
        )
        {
            return false;
        }

        if (IsUserMuted(playerId, out int _) || IsFloodGated(playerId, avatar, out int _))
        {
            return false;
        }

        await _roomGrain.SendComposerToRoomAsync(
            new RoomUseHabbiconMessageComposer
            {
                RoomIndex = objectId.Value,
                HabbiconId = habbiconId,
            }
        );

        return true;
    }

    private async Task SendChatAsync(
        RoomObjectId objectId,
        PlayerId playerId,
        string text,
        AvatarGestureType gesture,
        int styleId,
        List<(string, string, bool)> links,
        int trackingId,
        PlayerId? targetPlayerId,
        RoomChatType chatType
    )
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        // Every chat line comes through here — players, bots and pets alike — so this is the one
        // place the smiley-to-expression rule has to live. A caller that already knows the gesture
        // it wants (a wired action, the word quiz) keeps it.
        if (gesture is AvatarGestureType.None)
        {
            gesture = ChatGestures.FromText(text);
        }

        if (targetPlayerId is null)
        {
            // A shout is the same payload on a different header; the client draws it in the loud
            // bubble and reads it at any distance, which is the whole point of shouting.
            await _roomGrain.SendComposerToRoomAsync(
                chatType is RoomChatType.Shout
                    ? new ShoutMessageComposer
                    {
                        ObjectId = objectId,
                        Text = text,
                        Gesture = gesture,
                        StyleId = styleId,
                        Links = links,
                        TrackingId = trackingId,
                    }
                    : new ChatMessageComposer
                    {
                        ObjectId = objectId,
                        Text = text,
                        Gesture = gesture,
                        StyleId = styleId,
                        Links = links,
                        TrackingId = trackingId,
                    }
            );
        }
        else
        {
            WhisperMessageComposer whisperComposer = new WhisperMessageComposer
            {
                ObjectId = objectId,
                Text = text,
                Gesture = gesture,
                StyleId = styleId,
                Links = links,
                TrackingId = trackingId,
            };

            await Task.WhenAll(
                _roomGrain
                    ._grainFactory.GetPlayerPresenceGrain(playerId)
                    .SendComposerAsync(whisperComposer),
                _roomGrain
                    ._grainFactory.GetPlayerPresenceGrain(targetPlayerId.Value)
                    .SendComposerAsync(whisperComposer)
            );
        }

        await PersistChatAsync(playerId, targetPlayerId, text);
    }

    private async Task PersistChatAsync(PlayerId playerId, PlayerId? targetPlayerId, string text)
    {
        try
        {
            await using VortexDbContext dbCtx =
                await _roomGrain._dbCtxFactory.CreateDbContextAsync();

            dbCtx.Chatlogs.Add(
                new RoomChatlogEntity
                {
                    RoomEntityId = _roomGrain.RoomId,
                    PlayerEntityId = playerId,
                    TargetPlayerEntityId = targetPlayerId,
                    Message =
                        text.Length > MaxChatMessageLength ? text[..MaxChatMessageLength] : text,
                }
            );

            await dbCtx.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _roomGrain._logger.LogWarning(
                ex,
                "Failed to persist room chat log for room {RoomId}.",
                _roomGrain.RoomId
            );
        }
    }

    /// <summary>
    /// The hotel-wide word filter, before the room's own. Null means refused: the line reaches
    /// nobody, the speaker included, and the speaker is told why.
    /// </summary>
    internal async Task<string?> ApplyHotelFilterAsync(PlayerId playerId, string text)
    {
        WordFilterResult result = _roomGrain._wordFilter.Apply(
            text,
            WordFilterSource.RoomChat,
            playerId.Value
        );

        if (!result.Refused)
        {
            return result.Text;
        }

        await _roomGrain
            ._grainFactory.GetPlayerPresenceGrain(playerId)
            .SendComposerAsync(WordFilterNotice.Create(_roomGrain._wordFilter.RefusedMessage))
            .ConfigureAwait(true);

        return null;
    }

    /// <summary>
    /// Masks the room's own banned words. Whole words only, so a room that bans "ass" does not
    /// mangle "class" — the client's own filter behaves the same way, and an owner who wanted the
    /// substring can ban it on its own.
    /// </summary>
    internal string ApplyFilterWords(string text)
    {
        if (_roomGrain._state.FilterWords.Count == 0 || string.IsNullOrEmpty(text))
        {
            return text;
        }

        string[] parts = text.Split(' ');
        bool changed = false;

        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0 || !_roomGrain._state.FilterWords.Contains(parts[i]))
            {
                continue;
            }

            parts[i] = _roomGrain._state.FilterReplacement;
            changed = true;
        }

        return changed ? string.Join(' ', parts) : text;
    }

    /// <summary>
    /// Both mutes that can silence a line here: this room's own, and the hotel-wide staff sanction
    /// the player carries between rooms. The longer of the two wins, so walking next door cannot
    /// shorten a sanction. Both are in-memory reads — nothing on the chat path goes to the database.
    /// </summary>
    internal bool IsUserMuted(PlayerId playerId, out int secondsRemaining)
    {
        DateTime now = DateTime.UtcNow;

        DateTime? roomMute = ReadMute(_roomGrain._state.MuteExpiresUtc, playerId, now);
        DateTime? hotelMute = ReadMute(_roomGrain._state.HotelMuteExpiresUtc, playerId, now);

        DateTime? effective = (roomMute, hotelMute) switch
        {
            (null, null) => null,
            (DateTime room, null) => room,
            (null, DateTime hotel) => hotel,
            (DateTime room, DateTime hotel) => room > hotel ? room : hotel,
        };

        if (effective is null)
        {
            secondsRemaining = 0;
            return false;
        }

        secondsRemaining = (int)Math.Ceiling((effective.Value - now).TotalSeconds);
        return true;
    }

    /// <summary>Reads one mute table, retiring the entry when it has run out.</summary>
    private static DateTime? ReadMute(
        Dictionary<PlayerId, DateTime> mutes,
        PlayerId playerId,
        DateTime now
    )
    {
        if (!mutes.TryGetValue(playerId, out DateTime expiresUtc))
        {
            return null;
        }

        if (expiresUtc > now)
        {
            return expiresUtc;
        }

        mutes.Remove(playerId);
        return null;
    }

    /// <summary>
    /// Whether this line should be refused as flooding.
    /// <para>
    /// The allowance is what makes this flood control rather than a rate limit. Refusing every line
    /// that arrives inside the interval blocks the second half of any sentence somebody types
    /// quickly — the player sees the chat swallow their words, not a protection working.
    /// </para>
    /// </summary>
    internal bool IsFloodGated(PlayerId playerId, IRoomAvatar avatar, out int secondsRemaining)
    {
        secondsRemaining = 0;

        // Staff are exempt. Someone answering a room full of people, or pasting a rule, is doing
        // the job the flood limit exists to protect — and being gated mid-sentence while moderating
        // is worse than the flood.
        if (avatar is RoomPlayerAvatar { IsModerator: true })
        {
            return false;
        }

        int[] intervals = _roomGrain._roomConfig.ChatFloodIntervalSeconds;
        int sensitivityIndex = (int)_roomGrain._state.RoomSnapshot.ChatSettings.FloodSensitivity;
        int intervalSeconds =
            intervals.Length == 0 ? 0
            : sensitivityIndex >= 0 && sensitivityIndex < intervals.Length
                ? intervals[sensitivityIndex]
            : intervals[^1];

        long nowMs = Environment.TickCount64;

        if (intervalSeconds <= 0)
        {
            return false;
        }

        int allowance = Math.Max(1, _roomGrain._roomConfig.ChatFloodAllowance);
        long requiredMs = intervalSeconds * 1000L;

        if (!_floodStateByPlayer.TryGetValue(playerId, out (long LastMs, int Burst) state))
        {
            _floodStateByPlayer[playerId] = (nowMs, 1);
            return false;
        }

        long elapsedMs = nowMs - state.LastMs;

        // A gap at or beyond the interval means they paused; the burst starts over rather than
        // decaying, so a player is never carrying a penalty from a conversation minutes ago.
        if (elapsedMs >= requiredMs)
        {
            _floodStateByPlayer[playerId] = (nowMs, 1);
            return false;
        }

        if (state.Burst < allowance)
        {
            // Inside the window but still within budget. The timestamp deliberately does not move:
            // the window is measured from the first line of the burst, otherwise a player typing
            // just under the interval forever would never exhaust anything.
            _floodStateByPlayer[playerId] = (state.LastMs, state.Burst + 1);
            return false;
        }

        secondsRemaining = (int)Math.Ceiling((requiredMs - elapsedMs) / 1000.0);

        return true;
    }
}
