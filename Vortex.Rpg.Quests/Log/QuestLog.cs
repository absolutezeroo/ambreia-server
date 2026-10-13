using System;
using System.Collections.Generic;
using System.Linq;
using Vortex.Primitives.Rpg.Quests;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Quests.Objectives;

namespace Vortex.Rpg.Quests.Log;

/// <summary>
/// One player's quests (spec M2a §3.3), in memory: accept, count signals, read held objectives, hand
/// in, abandon. No clock, no store, no grain: the grain feeds it and saves what changed. A quest the
/// content no longer has is kept as it is and never moves (an import can bring it back).
/// </summary>
public sealed class QuestLog(QuestBook book, IEnumerable<QuestEntry> entries)
{
    public const int MaxActive = 20;

    private readonly Dictionary<string, QuestEntry> _entries = entries.ToDictionary(e => e.QuestId);

    public IReadOnlyCollection<QuestEntry> Entries => _entries.Values;

    /// <summary>Whether an active quest has an objective read from holdings: only then are they fetched.</summary>
    public bool HasHeld =>
        _entries.Values.Any(e =>
            e.Status != QuestStatus.Done
            && book.Quest(e.QuestId) is { } q
            && q.Objectives.Any(o => book.Kind(o.Kind) is IHeldObjectiveKind)
        );

    public QuestStatus StatusOf(string questId) =>
        _entries.TryGetValue(questId, out QuestEntry? e) ? e.Status : QuestStatus.None;

    /// <summary>The quest's requirements are the caller's (the room's rules); here: known, not taken yet, room left.</summary>
    public string Accept(string questId, DateTime now)
    {
        if (_entries.ContainsKey(questId) || book.Quest(questId) is not { Retired: false } quest)
        {
            return QuestCodes.NotOfferable;
        }

        if (_entries.Values.Count(e => e.Status != QuestStatus.Done) >= MaxActive)
        {
            return QuestCodes.LogFull;
        }

        QuestEntry entry = new()
        {
            QuestId = questId,
            Status = QuestStatus.Active,
            Progress = new int[quest.Objectives.Count],
            AcceptedAt = now,
        };

        _entries[questId] = entry;
        Settle(entry, quest);

        return QuestCodes.Ok;
    }

    public bool Count(QuestSignal signal)
    {
        bool changed = false;

        foreach ((QuestEntry entry, QuestDefinition quest) in Open())
        {
            for (int i = 0; i < quest.Objectives.Count; i++)
            {
                QuestObjectiveDefinition o = quest.Objectives[i];

                if (
                    book.Kind(o.Kind) is not ICountedObjectiveKind kind
                    || entry.Progress[i] >= o.Count
                )
                {
                    continue;
                }

                int add = kind.Counts(o, signal);

                if (add > 0)
                {
                    entry.Progress[i] = Math.Min(o.Count, entry.Progress[i] + add);
                    changed = true;
                }
            }

            changed |= Settle(entry, quest);
        }

        return changed;
    }

    public bool Hold(QuestHoldings holdings)
    {
        bool changed = false;

        foreach ((QuestEntry entry, QuestDefinition quest) in Open())
        {
            for (int i = 0; i < quest.Objectives.Count; i++)
            {
                QuestObjectiveDefinition o = quest.Objectives[i];

                if (book.Kind(o.Kind) is not IHeldObjectiveKind kind)
                {
                    continue;
                }

                int now = Math.Min(o.Count, kind.Current(o, holdings));

                if (now != entry.Progress[i])
                {
                    entry.Progress[i] = now;
                    changed = true;
                }
            }

            changed |= Settle(entry, quest);
        }

        return changed;
    }

    public string CanComplete(string questId) =>
        _entries.TryGetValue(questId, out QuestEntry? e)
        && e.Status == QuestStatus.Ready
        && book.Quest(questId) is not null
            ? QuestCodes.Ok
            : QuestCodes.NotReady;

    public void MarkDone(string questId, DateTime now)
    {
        QuestEntry entry = _entries[questId];

        entry.Status = QuestStatus.Done;
        entry.CompletedAt = now;
    }

    public string Abandon(string questId)
    {
        if (!_entries.TryGetValue(questId, out QuestEntry? e) || e.Status == QuestStatus.Done)
        {
            return QuestCodes.NotInLog;
        }

        _entries.Remove(questId);

        return QuestCodes.Ok;
    }

    /// <summary>Quests under way whose content is known, their counters widened to the content's objectives.</summary>
    private IEnumerable<(QuestEntry, QuestDefinition)> Open()
    {
        foreach (QuestEntry entry in _entries.Values)
        {
            if (entry.Status == QuestStatus.Done || book.Quest(entry.QuestId) is not { } quest)
            {
                continue;
            }

            if (entry.Progress.Length != quest.Objectives.Count)
            {
                int[] widened = new int[quest.Objectives.Count];
                Array.Copy(
                    entry.Progress,
                    widened,
                    Math.Min(entry.Progress.Length, widened.Length)
                );
                entry.Progress = widened;
            }

            yield return (entry, quest);
        }
    }

    /// <summary>Ready when every counter is full, active otherwise; true when that changed.</summary>
    private static bool Settle(QuestEntry entry, QuestDefinition quest)
    {
        QuestStatus status = quest
            .Objectives.Select((o, i) => entry.Progress[i] >= o.Count)
            .All(full => full)
            ? QuestStatus.Ready
            : QuestStatus.Active;

        if (status == entry.Status)
        {
            return false;
        }

        entry.Status = status;

        return true;
    }
}
