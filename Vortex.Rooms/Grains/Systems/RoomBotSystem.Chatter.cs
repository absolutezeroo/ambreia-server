using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Database.Entities.Room;
using Vortex.Primitives.Bots;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Snapshots.Avatars;
using Vortex.Protocol.Messages.Outgoing.Room.Chat;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;

namespace Vortex.Rooms.Grains.Systems;

/// <summary>
/// What makes a placed bot more than scenery: it says the phrases its owner configured. Kept apart
/// from placement because this runs on the room tick and that does not.
/// </summary>
public sealed partial class RoomBotSystem
{
    private readonly Dictionary<int, long> _nextChatterAtMsByBotId = [];

    /// <summary>Chatter settings as loaded, so a talking bot is not a database read per tick.</summary>
    private readonly Dictionary<int, BotChatterConfiguration> _chatterByBotId = [];

    /// <summary>Raw skills as loaded, shared with the motion half so both read one cache.</summary>
    private readonly Dictionary<int, Dictionary<string, string>> _skillsByBotId = [];

    private bool _skillsLoaded;

    public async Task ProcessBotsAsync(long now, CancellationToken ct)
    {
        if (now < _roomGrain._state.NextBotBoundaryMs)
        {
            return;
        }

        _roomGrain._state.NextBotBoundaryMs = _roomGrain.AdvanceBoundaryPast(now, BotTickMs);

        await EnsureBotsLoadedAsync(ct).ConfigureAwait(true);

        if (_botsById.Count == 0)
        {
            return;
        }

        await EnsureSkillsLoadedAsync(ct).ConfigureAwait(true);

        List<RoomAvatarSnapshot> moved = StepBots(now);

        if (moved.Count > 0)
        {
            await _roomGrain
                .SendComposerToRoomAsync(new UserUpdateMessageComposer { Avatars = [.. moved] })
                .ConfigureAwait(true);
        }

        foreach (BotSnapshot bot in _botsById.Values.OrderBy(b => b.BotId).ToArray())
        {
            if (
                !_chatterByBotId.TryGetValue(bot.BotId, out BotChatterConfiguration? chatter)
                || !chatter.AutoChat
                || chatter.Phrases.Length == 0
            )
            {
                continue;
            }

            // First sight of a bot schedules it rather than making it speak, so a room full of bots
            // does not greet every visitor in chorus the moment it activates.
            if (!_nextChatterAtMsByBotId.TryGetValue(bot.BotId, out long dueAt))
            {
                _nextChatterAtMsByBotId[bot.BotId] = ScheduleNextChatterAt(now, chatter);
                continue;
            }

            if (now < dueAt)
            {
                continue;
            }

            _nextChatterAtMsByBotId[bot.BotId] = ScheduleNextChatterAt(now, chatter);

            await _roomGrain
                .SendComposerToRoomAsync(
                    new ChatMessageComposer
                    {
                        ObjectId = ToRoomObjectId(bot.BotId),
                        Text = chatter.Phrases[Random.Shared.Next(chatter.Phrases.Length)],
                        Gesture = default,
                        Links = [],
                        TrackingId = 0,
                    }
                )
                .ConfigureAwait(true);
        }
    }

    /// <summary>
    /// The owner's delay, jittered by up to a quarter of itself. Bots sharing a delay would fall
    /// into lockstep and speak as one, which reads as a bug even though each is behaving.
    /// </summary>
    private static long ScheduleNextChatterAt(long now, BotChatterConfiguration chatter)
    {
        long delayMs = chatter.DelaySeconds * 1_000L;

        return now + delayMs + Random.Shared.Next((int)(delayMs / 4) + 1);
    }

    private async Task EnsureSkillsLoadedAsync(CancellationToken ct)
    {
        if (_skillsLoaded)
        {
            return;
        }

        await using VortexDbContext dbCtx = await _roomGrain
            ._dbCtxFactory.CreateDbContextAsync(ct)
            .ConfigureAwait(true);

        BotEntity[] bots = await dbCtx
            .Bots.AsNoTracking()
            .Where(b => b.RoomEntityId == _roomGrain.RoomId.Value && b.DeletedAt == null)
            .ToArrayAsync(ct)
            .ConfigureAwait(true);

        _chatterByBotId.Clear();
        _skillsByBotId.Clear();

        foreach (BotEntity bot in bots)
        {
            Dictionary<string, string> skills = ReadSkills(bot);
            _skillsByBotId[bot.Id] = skills;

            _chatterByBotId[bot.Id] = BotChatterConfiguration.Parse(
                skills.GetValueOrDefault(
                    BotSkillId.Chatter.ToString(CultureInfo.InvariantCulture),
                    string.Empty
                )
            );
        }

        _skillsLoaded = true;
    }

    /// <summary>
    /// Makes a bot say a line to the room on somebody else's account rather than its own chatter
    /// clock.
    /// </summary>
    public async Task SayAsync(int botId, string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        await EnsureBotsLoadedAsync(ct).ConfigureAwait(true);

        if (!_botsById.ContainsKey(botId))
        {
            return;
        }

        await _roomGrain
            .SendComposerToRoomAsync(
                new ChatMessageComposer
                {
                    ObjectId = ToRoomObjectId(botId),
                    Text = text,
                    Gesture = default,
                    Links = [],
                    TrackingId = 0,
                }
            )
            .ConfigureAwait(true);
    }

    /// <summary>Drops a bot's cached chatter, flags and plan so the next tick reloads them.</summary>
    private void InvalidateBotCaches(int botId)
    {
        _ = _chatterByBotId.Remove(botId);
        _ = _skillsByBotId.Remove(botId);
        _ = _nextChatterAtMsByBotId.Remove(botId);
        _ = _pathByBotId.Remove(botId);
        _ = _nextWanderAtMsByBotId.Remove(botId);
        _skillsLoaded = false;
    }
}
