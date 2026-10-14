using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Logging.Extensions;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Events;
using Vortex.Primitives.Rooms.Events.Player;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rpg.Quests;
using Vortex.Primitives.World.Rules;
using Vortex.Protocol.Messages.Outgoing.Rpg.Quests;
using Vortex.Rpg.Dialogues;
using Vortex.Rpg.Npcs;

namespace Vortex.Rooms.Grains.Modules;

/// <summary>
/// Talking to the zone's NPCs (spec M2a §3.3): reach (1 tile to open, 2 to choose), one dialogue per
/// player, the choice's position mapped to the content's index, the markers. What the dialogue says
/// and does is <see cref="INpcDialogues"/>'s: the room knows no content and no quest.
/// </summary>
public sealed class RoomDialogueModule(RoomGrain roomGrain, INpcDialogues dialogues)
    : IRoomEventListener
{
    private const int OpenReach = 1;
    private const int ChooseReach = 2;

    private readonly Dictionary<PlayerId, DialogueSession> _sessions = [];

    public async Task TalkAsync(PlayerId player, int objectId, CancellationToken ct)
    {
        if (roomGrain.NpcModule.NpcAt(objectId) is not { } npc || Avatar(player) is not { } avatar)
        {
            await SendAsync(
                player,
                new RpgQuestResultMessageComposer
                {
                    Action = "talk",
                    Code = "npc.unknown",
                    QuestId = string.Empty,
                }
            );

            return;
        }

        if (npc.Dialogue is not { } dialogueId)
        {
            return;
        }

        if (Distance(avatar, npc) <= OpenReach)
        {
            await OpenAsync(player, npc, dialogueId);

            return;
        }

        if (Approach(avatar, npc) is not { } cell)
        {
            await SendAsync(
                player,
                new RpgQuestResultMessageComposer
                {
                    Action = "talk",
                    Code = "npc.too_far",
                    QuestId = string.Empty,
                }
            );

            return;
        }

        await roomGrain.AvatarModule.WalkAvatarToThenAsync(
            avatar,
            cell.X,
            cell.Y,
            _ =>
                Avatar(player) is { } there && Distance(there, npc) <= OpenReach
                    ? OpenAsync(player, npc, dialogueId)
                    : Task.CompletedTask,
            ct
        );
    }

    public async Task ChooseAsync(PlayerId player, int position, CancellationToken ct)
    {
        if (!_sessions.TryGetValue(player, out DialogueSession? session))
        {
            return;
        }

        if (
            roomGrain.NpcModule.NpcAt(session.NpcObjectId) is not { } npc
            || Avatar(player) is not { } avatar
            || Distance(avatar, npc) > ChooseReach
        )
        {
            _sessions.Remove(player);
            await SendAsync(player, new RpgDialogueCloseMessageComposer());

            return;
        }

        if (position < 0 || position >= session.Shown.Choices.Length)
        {
            return;
        }

        DialogueStep step = await dialogues.ChooseAsync(
            Conversation(player, session.NpcId, session.DialogueId),
            session.Shown.NodeId,
            session.Shown.Choices[position].Index
        );

        if (step.IsIgnored)
        {
            return;
        }

        if (step.Next is { } next)
        {
            _sessions[player] = session with { Shown = next };
            await SendAsync(player, Shown(session.NpcObjectId, next));

            return;
        }

        _sessions.Remove(player);
        await SendAsync(player, new RpgDialogueCloseMessageComposer());

        if (step.Action is { } action)
        {
            await SendAsync(
                player,
                new RpgQuestResultMessageComposer
                {
                    Action = action,
                    Code = step.Code,
                    QuestId = step.QuestId,
                }
            );
        }
    }

    /// <summary>What each NPC has for this player; sent on entry and whenever their quest part changes.</summary>
    public async Task SendMarkersAsync(PlayerId player)
    {
        if (roomGrain.NpcModule.Npcs.Count == 0)
        {
            return;
        }

        await SendAsync(
            player,
            new RpgNpcsMessageComposer
            {
                Npcs =
                [
                    .. roomGrain.NpcModule.Npcs.Select(npc => new RpgNpcMarker
                    {
                        ObjectId = npc.ObjectId.Value,
                        Marker = npc.Dialogue is { } d
                            ? dialogues.Marker(Conversation(player, npc.NpcId, d))
                            : RpgNpcMarker.None,
                    }),
                ],
            }
        );
    }

    public Task OnRoomEventAsync(RoomEvent evt, CancellationToken ct)
    {
        if (evt is PlayerLeftEvent left)
        {
            _sessions.Remove(left.PlayerId);
        }

        return Task.CompletedTask;
    }

    private async Task OpenAsync(PlayerId player, IRoomNpc npc, string dialogueId)
    {
        if (await dialogues.OpenAsync(Conversation(player, npc.NpcId, dialogueId)) is not { } view)
        {
            return;
        }

        _sessions[player] = new DialogueSession(npc.ObjectId.Value, npc.NpcId, dialogueId, view);
        await SendAsync(player, Shown(npc.ObjectId.Value, view));
    }

    private NpcConversation Conversation(PlayerId player, string npcId, string dialogueId) =>
        new(
            player,
            roomGrain._state.Zone?.Settings.Id ?? string.Empty,
            npcId,
            dialogueId,
            QuestStates(player),
            conditions => roomGrain.ZoneTriggers.ConditionsHold(conditions, player)
        );

    private IReadOnlyDictionary<string, string> QuestStates(PlayerId player) =>
        roomGrain._state.ZoneRuntime is { } zone
        && RoomGrain
            .PartsOf(zone.Facts, player)
            .TryGetValue(typeof(QuestZoneFacts), out ZoneFacts? part)
            ? ((QuestZoneFacts)part).States
            : ImmutableDictionary<string, string>.Empty;

    private static RpgDialogueMessageComposer Shown(int npcObjectId, DialogueNodeView view) =>
        new()
        {
            NpcObjectId = npcObjectId,
            Speaker = view.Speaker,
            Text = view.Text,
            Choices =
            [
                .. view.Choices.Select(c => new RpgDialogueChoice { Text = c.Text, Kind = c.Kind }),
            ],
        };

    private IRoomPlayer? Avatar(PlayerId player) =>
        roomGrain._state.AvatarsByPlayerId.TryGetValue(player, out RoomObjectId id)
        && roomGrain._state.AvatarsByObjectId.TryGetValue(id, out IRoomAvatar? avatar)
            ? avatar as IRoomPlayer
            : null;

    private static int Distance(IRoomAvatar a, IRoomAvatar b) =>
        Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    /// <summary>The free cell next to the NPC nearest the player.</summary>
    private (int X, int Y)? Approach(IRoomAvatar player, IRoomNpc npc)
    {
        RoomMapModule map = roomGrain.MapModule;

        return Enumerable
            .Range(-1, 3)
            .SelectMany(dx => Enumerable.Range(-1, 3).Select(dy => (X: npc.X + dx, Y: npc.Y + dy)))
            .Where(c =>
                (c.X, c.Y) != (npc.X, npc.Y)
                && map.InBounds(c.X, c.Y)
                && map.IsSpawnable(map.ToIdx(c.X, c.Y))
            )
            .OrderBy(c => Math.Abs(c.X - player.X) + Math.Abs(c.Y - player.Y))
            .Select(c => ((int X, int Y)?)c)
            .FirstOrDefault();
    }

    private Task SendAsync(PlayerId player, IComposer composer) =>
        roomGrain._grainFactory.GetPlayerPresenceGrain(player).SendComposerAsync(composer);
}
