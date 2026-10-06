using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Dashboard.API.Api.Hotel.Contracts;
using Vortex.Database.Context;

namespace Vortex.Dashboard.API.Api.Hotel;

internal sealed class PetReads(IDbContextFactory<VortexDbContext> dbContextFactory)
    : DashboardReads(dbContextFactory)
{
    /// <summary>Read-only overview of the pets domain: population, type/race distribution and
    /// average level. There is no dedicated pet audit category today, so this reads straight off
    /// <c>PetEntity</c> rather than an audit trail.</summary>
    public Task<PetStats> PetsStatsAsync(NameValueCollection query, CancellationToken ct) =>
        QueryAsync<PetStats>(
            async db =>
            {
                (DateTime since, DateTime until) = TimeWindow.Resolve(query, DateTime.UtcNow);
                string granularity = TimeWindow.Granularity(query["granularity"]);

                List<PetStatsRow> rows = await db
                    .Pets.AsNoTracking()
                    .Select(p => new PetStatsRow(
                        p.CreatedAt,
                        p.Type,
                        p.Race,
                        p.Level,
                        p.OwnerPlayerEntityId
                    ))
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                int totalPets = rows.Count;
                double avgLevel = totalPets > 0 ? Math.Round(rows.Average(r => r.Level), 2) : 0d;

                List<PetTypeCount> byType = rows.GroupBy(r => r.Type)
                    .Select(g => new PetTypeCount(g.Key, g.Count()))
                    .OrderByDescending(g => g.Count)
                    .ToList();

                List<PetRaceCount> byRace = rows.GroupBy(r => new { r.Type, r.Race })
                    .Select(g => new PetRaceCount(g.Key.Type, g.Key.Race, g.Count()))
                    .OrderByDescending(g => g.Count)
                    .Take(20)
                    .ToList();

                Dictionary<DateTime, int> bucketMap = new();
                DateTime cursor = TimeWindow.Bucket(since, granularity);
                DateTime end = TimeWindow.Bucket(until, granularity);

                while (cursor <= end)
                {
                    bucketMap[cursor] = 0;
                    cursor = TimeWindow.NextBucket(cursor, granularity);
                }

                foreach (
                    PetStatsRow row in rows.Where(r => r.CreatedAt >= since && r.CreatedAt <= until)
                )
                {
                    DateTime bucket = TimeWindow.Bucket(row.CreatedAt, granularity);
                    bucketMap[bucket] = bucketMap.GetValueOrDefault(bucket) + 1;
                }

                List<PetGrowthPoint> growth = bucketMap
                    .OrderBy(pair => pair.Key)
                    .Select(pair => new PetGrowthPoint(
                        pair.Key.ToString("O"),
                        TimeWindow.Label(pair.Key, granularity),
                        pair.Value
                    ))
                    .ToList();

                var topOwners = await db
                    .Pets.AsNoTracking()
                    .GroupBy(p => p.OwnerPlayerEntityId)
                    .Select(g => new { ownerId = g.Key, petCount = g.Count() })
                    .OrderByDescending(g => g.petCount)
                    .Take(10)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                List<int> ownerIds = DisplayNameQueries.NormalizeIds(
                    topOwners.Select(o => (int?)o.ownerId)
                );
                Dictionary<int, string> ownerNames = await db.PlayerNamesAsync(ownerIds, ct)
                    .ConfigureAwait(false);

                List<PetOwnerCount> topOwnersWithNames = topOwners
                    .Select(o => new PetOwnerCount(
                        o.ownerId,
                        DisplayNameQueries.ResolvePlayerName(ownerNames, (int?)o.ownerId),
                        o.petCount
                    ))
                    .ToList();

                return new PetStats(
                    new ReportWindow(since, until, granularity),
                    new PetTotals(totalPets, avgLevel),
                    byType,
                    byRace,
                    growth,
                    topOwnersWithNames
                );
            },
            ct
        );

    private sealed record PetStatsRow(
        DateTime CreatedAt,
        int Type,
        int Race,
        int Level,
        int OwnerPlayerEntityId
    );
}
