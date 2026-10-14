using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Database.Context;
using Vortex.Logging.Extensions;
using Vortex.Primitives.Events;
using Vortex.Primitives.Events.Rpg;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rpg.Characters.Grains;
using Vortex.Primitives.Rpg.Quests;
using Vortex.Primitives.Rpg.Quests.Grains;
using Vortex.Rpg.Content;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Rules;
using Vortex.Rpg.Quests.Log;
using Vortex.Rpg.Quests.Objectives;
using Vortex.Rpg.Quests.Persistence;
using Vortex.Rpg.Quests.Rewards;
using Vortex.Rpg.Quests.Wire;

namespace Vortex.Rpg.Quests.Grains;

/// <summary>
/// One player's quest log (spec M2a §3.3): read from rpg_character_quests on activation, written on
/// every change, told to the zone (facts) and to the bus (QuestLogChangedEvent). The rules are
/// <see cref="QuestLog"/>'s; this one feeds it and grants. A hand-in writes done before granting, so
/// a retry finds it done: at most once.
/// </summary>
internal sealed class QuestLogGrain(
    IDbContextFactory<VortexDbContext> dbFactory,
    IRpgContent content,
    IEnumerable<IQuestObjectiveRule> objectives,
    IEnumerable<IQuestRewardKind> rewards,
    IGrainFactory grains,
    IEventPublisher events,
    ILogger<QuestLogGrain> logger
) : Grain, IQuestLogGrain
{
    private readonly Dictionary<string, IQuestObjectiveRule> _objectives = objectives.ToDictionary(
        k => k.Kind
    );
    private readonly Dictionary<string, IQuestRewardKind> _rewards = rewards.ToDictionary(k =>
        k.Kind
    );
    private readonly QuestLogStore _store = new(dbFactory);

    // ponytail: monotonic for one process, as the character's zone facts; several silos need a stored sequence.
    private long _factsSequence = Stopwatch.GetTimestamp();

    private PlayerId _player;
    private QuestLog _log = null!;

    public TimeProvider Time { get; set; } = TimeProvider.System;

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        _player = PlayerId.Parse((int)this.GetPrimaryKeyLong());
        _log = new QuestLog(
            new QuestBook(
                id => content.Get<QuestDefinition>(id),
                kind => _objectives.GetValueOrDefault(kind)
            ),
            await _store.LoadAsync(_player, ct)
        );
    }

    public Task<QuestLogSnapshot> GetLogAsync() =>
        Task.FromResult(
            new QuestLogSnapshot([
                .. _log.Entries.Select(e => new QuestEntrySnapshot(
                    e.QuestId,
                    e.Status,
                    [.. e.Progress]
                )),
            ])
        );

    public async Task<string> AcceptAsync(string questId)
    {
        string code = _log.Accept(questId, Now());

        if (code == QuestCodes.Ok)
        {
            await HoldAsync();
            await ChangedAsync();
        }

        return code;
    }

    public async Task<string> CompleteAsync(string questId)
    {
        // Held objectives are read again: shards sold since the last refresh make it active again.
        bool moved = await HoldAsync();

        if (
            _log.CanComplete(questId) != QuestCodes.Ok
            || content.Get<QuestDefinition>(questId) is not { } quest
        )
        {
            if (moved)
            {
                await ChangedAsync();
            }

            return QuestCodes.NotReady;
        }

        IPlayerCharacterGrain character = grains.GetPlayerCharacterGrain(_player);

        // Each held kind takes what it takes (collect, its consumed items): the grain names no kind.
        foreach (QuestObjectiveDefinition o in quest.Objectives)
        {
            if (_objectives.GetValueOrDefault(o.Kind) is IHeldObjectiveKind held)
            {
                await held.HandInAsync(o, character);
            }
        }

        _log.MarkDone(questId, Now());
        await ChangedAsync();

        for (int i = 0; i < quest.Rewards.Count; i++)
        {
            QuestRewardDefinition reward = quest.Rewards[i];

            try
            {
                await _rewards[reward.Kind]
                    .GrantAsync(
                        reward,
                        new QuestGrant(character, questId, $"quest:{questId}:{_player.Value}:{i}")
                    );
            }
            catch (Exception ex)
            {
                // Done is written: the player keeps the quest handed in; the operator sees what was lost.
                logger.LogError(
                    ex,
                    "Quest {QuestId} of player {PlayerId}: reward {Index} ({Kind}) was not granted",
                    questId,
                    _player.Value,
                    i,
                    reward.Kind
                );
            }
        }

        return QuestCodes.Ok;
    }

    public async Task<string> AbandonAsync(string questId)
    {
        string code = _log.Abandon(questId);

        if (code == QuestCodes.Ok)
        {
            await ChangedAsync();
        }

        return code;
    }

    public async Task CountAsync(QuestSignal signal)
    {
        if (_log.Count(signal))
        {
            await ChangedAsync();
        }
    }

    public async Task RefreshHeldAsync()
    {
        if (await HoldAsync())
        {
            await ChangedAsync();
        }
    }

    public Task<QuestZoneFacts> GetZoneFactsAsync() => Task.FromResult(Facts());

    public Task PushLogAsync() => SendLogAsync();

    /// <summary>RpgQuestLog to the player: a view, so its failure never undoes the change.</summary>
    private async Task SendLogAsync()
    {
        try
        {
            QuestLogSnapshot log = await GetLogAsync();

            await grains
                .GetPlayerPresenceGrain(_player)
                .SendComposerAsync(
                    QuestLogComposer.Of(log.Entries, id => content.Get<QuestDefinition>(id))
                );
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The quest log of player {PlayerId} was not sent", _player.Value);
        }
    }

    /// <summary>Reads holdings only when an open quest has a held objective; true when the log moved.</summary>
    private async Task<bool> HoldAsync() =>
        _log.HasHeld
        && _log.Hold(await QuestHoldingsReader.ReadAsync(grains.GetPlayerCharacterGrain(_player)));

    private async Task ChangedAsync()
    {
        await _store.SaveAsync(_player, _log.Entries, CancellationToken.None);
        await SendLogAsync();

        events
            .PublishAsync(new QuestLogChangedEvent(_player))
            .LogAndForget(logger, "Publishing the quest log of {PlayerId}", _player.Value);

        try
        {
            grains
                .GetPlayerPresenceGrain(_player)
                .OnZoneFactsChangedAsync(Facts(), CancellationToken.None)
                .LogAndForget(
                    logger,
                    "Passing the quest facts of player {PlayerId} to its zone",
                    _player.Value
                );
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Quest facts of player {PlayerId} not sent", _player.Value);
        }
    }

    private QuestZoneFacts Facts() =>
        new()
        {
            Sequence = ++_factsSequence,
            States = _log.Entries.ToImmutableDictionary(
                e => e.QuestId,
                e => QuestCodes.Name(e.Status)
            ),
        };

    private DateTime Now() => Time.GetUtcNow().UtcDateTime;
}
