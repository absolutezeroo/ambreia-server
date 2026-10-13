using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Database.Entities.Rpg;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rpg.Quests;
using Vortex.Rpg.Quests.Log;

namespace Vortex.Rpg.Quests.Persistence;

/// <summary>A player's quest rows (spec M2a §3.3): read once, then rewritten to match the log after each change.</summary>
public sealed class QuestLogStore(IDbContextFactory<VortexDbContext> dbFactory)
{
    public async Task<List<QuestEntry>> LoadAsync(PlayerId player, CancellationToken ct)
    {
        await using VortexDbContext db = await dbFactory.CreateDbContextAsync(ct);

        List<RpgCharacterQuestEntity> rows = await db
            .RpgCharacterQuests.AsNoTracking()
            .Where(r => r.PlayerEntityId == player.Value)
            .ToListAsync(ct);

        return [.. rows.Select(ToEntry)];
    }

    public async Task SaveAsync(
        PlayerId player,
        IReadOnlyCollection<QuestEntry> entries,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await dbFactory.CreateDbContextAsync(ct);

        Dictionary<string, RpgCharacterQuestEntity> rows = await db
            .RpgCharacterQuests.Where(r => r.PlayerEntityId == player.Value)
            .ToDictionaryAsync(r => r.QuestId, ct);

        foreach (QuestEntry entry in entries)
        {
            if (rows.Remove(entry.QuestId, out RpgCharacterQuestEntity? row))
            {
                row.Status = QuestCodes.Name(entry.Status);
                row.Progress = Join(entry.Progress);
                row.CompletedAt = entry.CompletedAt;
            }
            else
            {
                db.RpgCharacterQuests.Add(
                    new RpgCharacterQuestEntity
                    {
                        PlayerEntityId = player.Value,
                        QuestId = entry.QuestId,
                        Status = QuestCodes.Name(entry.Status),
                        Progress = Join(entry.Progress),
                        AcceptedAt = entry.AcceptedAt,
                        CompletedAt = entry.CompletedAt,
                    }
                );
            }
        }

        // What is left was abandoned.
        db.RpgCharacterQuests.RemoveRange(rows.Values);
        await db.SaveChangesAsync(ct);
    }

    private static QuestEntry ToEntry(RpgCharacterQuestEntity row) =>
        new()
        {
            QuestId = row.QuestId,
            Status = Enum.Parse<QuestStatus>(row.Status, ignoreCase: true),
            Progress =
                row.Progress.Length == 0
                    ? []
                    :
                    [
                        .. row
                            .Progress.Split(',')
                            .Select(p => int.Parse(p, CultureInfo.InvariantCulture)),
                    ],
            AcceptedAt = DateTime.SpecifyKind(row.AcceptedAt, DateTimeKind.Utc),
            CompletedAt = row.CompletedAt is { } done
                ? DateTime.SpecifyKind(done, DateTimeKind.Utc)
                : null,
        };

    private static string Join(int[] progress) =>
        string.Join(',', progress.Select(p => p.ToString(CultureInfo.InvariantCulture)));
}
