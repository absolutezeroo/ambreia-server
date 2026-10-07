using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Dashboard.API.Api.Progression.Contracts;
using Vortex.Dashboard.API.Infrastructure;
using Vortex.Database.Context;

namespace Vortex.Dashboard.API.Api.Progression;

/// <summary>
/// The four fishing content tables plus what players have done with them, read for the page that
/// edits them. The writes live in <see cref="Operations.FishingOperations"/>.
/// </summary>
/// <remarks>
/// Zones carry their species count, because a zone with none is a spot that can be fished and never
/// yields anything — the one misconfiguration here that looks exactly like a bug from the outside.
/// </remarks>
internal sealed class FishingReads(
    IDbContextFactory<VortexDbContext> dbContextFactory,
    DashboardAssetUrls assetUrls
) : DashboardReads(dbContextFactory)
{
    private readonly DashboardAssetUrls _assetUrls = assetUrls;

    public Task<FishingContent> FishingContentAsync(CancellationToken ct) =>
        QueryAsync<FishingContent>(
            async db =>
            {
                var zoneRows = await db
                    .FishingZones.AsNoTracking()
                    .OrderBy(z => z.RequiredLevel)
                    .ThenBy(z => z.Id)
                    .Select(z => new
                    {
                        z.Id,
                        z.NameKey,
                        z.FurniClass,
                        z.RequiredLevel,
                        z.MinCatches,
                        z.MaxCatches,
                        speciesCount = db.FishingSpecies.Count(s => s.ZoneId == z.Id),
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                // The spot's artwork, so the operator recognises the furni instead of reading a
                // classname. BuildFurniIconUrl does not translate to SQL, so it is attached after.
                List<FishingZoneRow> zones = zoneRows
                    .Select(z => new FishingZoneRow(
                        z.Id,
                        z.NameKey,
                        z.FurniClass,
                        _assetUrls.FurniIcon(z.FurniClass),
                        z.RequiredLevel,
                        z.MinCatches,
                        z.MaxCatches,
                        z.speciesCount
                    ))
                    .ToList();

                var species = await db
                    .FishingSpecies.AsNoTracking()
                    .OrderBy(s => s.ZoneId)
                    .ThenBy(s => s.RequiredLevel)
                    .ThenBy(s => s.Id)
                    .Select(s => new
                    {
                        s.Id,
                        s.ZoneId,
                        s.NameKey,
                        s.RequiredLevel,
                        s.RarityStars,
                        s.CatchRate,
                        s.RarityWeight,
                        s.MinWeight,
                        s.MaxWeight,
                        s.XpReward,
                        s.GoldenXpBonus,
                        s.CurrencyReward,
                        s.ActiveHours,
                        s.ActiveWeekdays,
                        s.ActiveSeasons,
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                List<FishingRodTierRow> rodTiers = await db
                    .FishingRodTiers.AsNoTracking()
                    .OrderBy(t => t.Quality)
                    .Select(t => new FishingRodTierRow(
                        t.Id,
                        t.Quality,
                        t.XpThreshold,
                        t.NameKey,
                        t.HandItemId,
                        t.CatchMultiplier,
                        t.GoldenMultiplier,
                        t.HookHavocChance
                    ))
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                List<FishingLevelRow> levels = await db
                    .FishingLevels.AsNoTracking()
                    .OrderBy(l => l.Level)
                    .Select(l => new FishingLevelRow(l.Id, l.Level, l.XpThreshold))
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                // The share of the draw each species really has: it competes with the others in its
                // own zone, so the denominator is per zone rather than global. Computed here because
                // a page that worked it out itself would be a second implementation of the rule the
                // server draws by.
                Dictionary<int, long> zoneWeights = species
                    .GroupBy(s => s.ZoneId)
                    .ToDictionary(g => g.Key, g => g.Sum(s => (long)s.RarityWeight));

                return new FishingContent(
                    zones,
                    species
                        .Select(s => new FishingSpeciesRow(
                            s.Id,
                            s.ZoneId,
                            s.NameKey,
                            s.RequiredLevel,
                            s.RarityStars,
                            s.CatchRate,
                            Math.Round(s.CatchRate / 10.0, 1),
                            s.RarityWeight,
                            zoneWeights.GetValueOrDefault(s.ZoneId) > 0
                                ? Math.Round(s.RarityWeight * 100.0 / zoneWeights[s.ZoneId], 1)
                                : 0,
                            s.MinWeight,
                            s.MaxWeight,
                            s.XpReward,
                            s.GoldenXpBonus,
                            s.CurrencyReward,
                            s.ActiveHours,
                            s.ActiveWeekdays,
                            s.ActiveSeasons,
                            s.ActiveHours == 0xFFFFFF,
                            s.ActiveWeekdays == 0b1111111
                        ))
                        .ToList(),
                    rodTiers,
                    levels
                );
            },
            ct
        );

    /// <summary>
    /// What players have actually caught: the records board. Read-only — a record is
    /// something that happened, not something an operator sets.
    /// </summary>
    public Task<FishingActivity> FishingActivityAsync(
        NameValueCollection query,
        CancellationToken ct
    ) =>
        QueryAsync<FishingActivity>(
            async db =>
            {
                int limit = Math.Clamp(QueryValues.Int(query["limit"], 25), 1, 100);

                List<FishingRecordRow> records = await db
                    .FishingRecords.AsNoTracking()
                    .OrderByDescending(r => r.BestWeight)
                    .Take(limit)
                    .Select(r => new FishingRecordRow(
                        r.Id,
                        r.PlayerId,
                        db.Players.Where(p => p.Id == r.PlayerId)
                            .Select(p => p.Name)
                            .FirstOrDefault(),
                        r.SpeciesId,
                        db.FishingSpecies.Where(s => s.Id == r.SpeciesId)
                            .Select(s => s.NameKey)
                            .FirstOrDefault(),
                        r.BestWeight,
                        r.CaughtCount,
                        r.BestAt
                    ))
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                return new FishingActivity(
                    records,
                    await db.FishingPlayerState.CountAsync(ct).ConfigureAwait(false)
                );
            },
            ct
        );
}
