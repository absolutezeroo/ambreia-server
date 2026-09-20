using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Dashboard.API.Admin.Rules;
using Vortex.Database.Context;
using Vortex.Database.Entities.RewardTracks;
using Vortex.Primitives.Hosting;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.RewardTracks;
using Vortex.Primitives.RewardTracks.Admin;
using Vortex.Primitives.RewardTracks.Snapshots;
using Vortex.Primitives.Signals;

namespace Vortex.Dashboard.API.Admin.Progression;

/// <summary>
/// Content CRUD for reward tracks, plus the per-player operations an operator needs.
/// </summary>
/// <remarks>
/// <para>
/// The whole reason a campaign is content and not code. Everything the Introduction Track is —
/// its tasks, their stages, its milestones, its premium tier — an operator can build here without
/// a rebuild.
/// </para>
/// <para>
/// Every structural write bumps the track's content version and reloads the catalog. A player
/// looking at the track when it changes underneath them is pushed the list again with the client's
/// own reload flag, which is what that flag was put there for.
/// </para>
/// </remarks>
internal sealed class RewardTrackAdminService(
    IDbContextFactory<VortexDbContext> dbContextFactory,
    IRewardTrackCatalog catalog,
    IReferenceDataReloader reloader,
    IGrainFactory grainFactory,
    // What a filter is allowed to say. Read from the loaded translators rather than from a map kept
    // in step by hand, so the editor cannot offer a fact this action never emits.
    ISignalVocabulary vocabulary,
    ILogger<RewardTrackAdminService> logger
) : IRewardTrackAdminService
{
    public async Task<RewardTrackAdminResult> CreateTrackAsync(
        RewardTrackSpec spec,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(spec.TrackId))
        {
            return RewardTrackAdminResult.Fail("track_id_required");
        }

        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        if (
            await db
                .RewardTracks.AnyAsync(t => t.TrackId == spec.TrackId && t.DeletedAt == null, ct)
                .ConfigureAwait(false)
        )
        {
            return RewardTrackAdminResult.Fail("track_id_taken");
        }

        RewardTrackEntity row = new() { TrackId = spec.TrackId, ContentVersion = 1 };

        Apply(row, spec);

        // Always a draft, whatever the spec says. Publishing runs the validator, and a track that
        // arrived live would have skipped it.
        row.Status = RewardTrackStatus.Draft;

        db.RewardTracks.Add(row);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await reloader.ReloadAsync(IRewardTrackCatalog.CacheName, ct).ConfigureAwait(false);

        return RewardTrackAdminResult.Ok(row.Id);
    }

    public async Task<RewardTrackAdminResult> UpdateTrackAsync(
        int trackRowId,
        RewardTrackSpec spec,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        RewardTrackEntity? row = await db
            .RewardTracks.FirstOrDefaultAsync(t => t.Id == trackRowId && t.DeletedAt == null, ct)
            .ConfigureAwait(false);

        if (row is null)
        {
            return RewardTrackAdminResult.Fail("not_found");
        }

        // The content id is the identity every player row keys on. Changing it would orphan all of
        // them at once, which is the one edit that cannot be undone.
        if (!string.Equals(row.TrackId, spec.TrackId, StringComparison.Ordinal))
        {
            return RewardTrackAdminResult.Fail("track_id_immutable");
        }

        Apply(row, spec);
        row.ContentVersion++;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAndNotifyAsync(row.TrackId, ct).ConfigureAwait(false);

        return RewardTrackAdminResult.Ok(row.Id);
    }

    public async Task<RewardTrackAdminResult> CloneTrackAsync(
        int trackRowId,
        string newTrackId,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(newTrackId))
        {
            return RewardTrackAdminResult.Fail("track_id_required");
        }

        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        RewardTrackEntity? source = await db
            .RewardTracks.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == trackRowId && t.DeletedAt == null, ct)
            .ConfigureAwait(false);

        if (source is null)
        {
            return RewardTrackAdminResult.Fail("not_found");
        }

        if (
            await db
                .RewardTracks.AnyAsync(t => t.TrackId == newTrackId && t.DeletedAt == null, ct)
                .ConfigureAwait(false)
        )
        {
            return RewardTrackAdminResult.Fail("track_id_taken");
        }

        RewardTrackEntity clone = new()
        {
            TrackId = newTrackId,
            Theme = source.Theme,
            // A clone is always a draft: last season's dates are on it and would either be in the
            // past or, worse, still open.
            Status = RewardTrackStatus.Draft,
            SortOrder = source.SortOrder,
            UnlockKind = source.UnlockKind,
            UnlockValue = source.UnlockValue,
            CompletionPolicy = source.CompletionPolicy,
            PremiumEnabled = source.PremiumEnabled,
            PremiumBoostPerMille = source.PremiumBoostPerMille,
            PremiumInstantPoints = source.PremiumInstantPoints,
            PremiumCostCredits = source.PremiumCostCredits,
            PremiumCostDiamonds = source.PremiumCostDiamonds,
            Hidden = source.Hidden,
            CampaignCode = source.CampaignCode,
            ContentVersion = 1,
        };

        db.RewardTracks.Add(clone);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await CloneTasksAsync(db, trackRowId, clone.Id, ct).ConfigureAwait(false);
        await ClonePrizesAsync(db, trackRowId, clone.Id, ct).ConfigureAwait(false);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await reloader.ReloadAsync(IRewardTrackCatalog.CacheName, ct).ConfigureAwait(false);

        logger.LogInformation(
            "Cloned reward track {Source} into {Target} as a draft.",
            source.TrackId,
            newTrackId
        );

        return RewardTrackAdminResult.Ok(clone.Id);
    }

    public async Task<RewardTrackAdminResult> PublishTrackAsync(
        int trackRowId,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        RewardTrackEntity? row = await db
            .RewardTracks.FirstOrDefaultAsync(t => t.Id == trackRowId && t.DeletedAt == null, ct)
            .ConfigureAwait(false);

        if (row is null)
        {
            return RewardTrackAdminResult.Fail("not_found");
        }

        // Validated before it goes live, never after. A published track with an unreachable
        // milestone is a support ticket per player; an unpublished one is an afternoon's editing.
        RewardTrackContentReport report = await ValidateAsync(ct).ConfigureAwait(false);

        if (
            report.Problems.Any(p =>
                string.Equals(p.TrackId, row.TrackId, StringComparison.Ordinal)
            )
        )
        {
            return RewardTrackAdminResult.Fail("content_invalid");
        }

        row.Status =
            row.StartsAt is { } starts && starts > DateTime.UtcNow
                ? RewardTrackStatus.Scheduled
                : RewardTrackStatus.Active;
        row.ContentVersion++;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAndNotifyAsync(row.TrackId, ct).ConfigureAwait(false);

        logger.LogInformation(
            "Published reward track {TrackId} as {Status}.",
            row.TrackId,
            row.Status
        );

        return RewardTrackAdminResult.Ok(row.Id);
    }

    public async Task<RewardTrackAdminResult> ArchiveTrackAsync(
        int trackRowId,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        RewardTrackEntity? row = await db
            .RewardTracks.FirstOrDefaultAsync(t => t.Id == trackRowId && t.DeletedAt == null, ct)
            .ConfigureAwait(false);

        if (row is null)
        {
            return RewardTrackAdminResult.Fail("not_found");
        }

        row.Status = RewardTrackStatus.Archived;
        row.ContentVersion++;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAndNotifyAsync(row.TrackId, ct).ConfigureAwait(false);

        return RewardTrackAdminResult.Ok(row.Id);
    }

    public async Task<RewardTrackAdminResult> DeleteTrackAsync(int trackRowId, CancellationToken ct)
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        RewardTrackEntity? row = await db
            .RewardTracks.FirstOrDefaultAsync(t => t.Id == trackRowId, ct)
            .ConfigureAwait(false);

        if (row is null)
        {
            return RewardTrackAdminResult.Fail("not_found");
        }

        // Refused while anyone has progress. Player rows key on the content id rather than a
        // foreign key precisely so a rebuild does not delete them, and deleting the definition
        // would leave them pointing at nothing. Archive is the operation for a finished campaign.
        if (
            await db
                .PlayerRewardTracks.AnyAsync(t => t.TrackId == row.TrackId, ct)
                .ConfigureAwait(false)
        )
        {
            return RewardTrackAdminResult.Fail("players_have_progress");
        }

        List<RewardTrackTaskEntity> tasks = await db
            .RewardTrackTasks.Where(t => t.RewardTrackEntityId == trackRowId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        List<int> taskIds = [.. tasks.Select(t => t.Id)];

        List<RewardTrackPrizeEntity> prizes = await db
            .RewardTrackPrizes.Where(p => p.RewardTrackEntityId == trackRowId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        List<int> prizeIds = [.. prizes.Select(p => p.Id)];

        db.RewardTrackTaskLevels.RemoveRange(
            await db
                .RewardTrackTaskLevels.Where(l => taskIds.Contains(l.RewardTrackTaskEntityId))
                .ToListAsync(ct)
                .ConfigureAwait(false)
        );
        List<int> stepIds =
        [
            .. await db
                .RewardTrackTaskSteps.Where(s => taskIds.Contains(s.RewardTrackTaskEntityId))
                .Select(s => s.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false),
        ];

        db.RewardTrackStepFilters.RemoveRange(
            await db
                .RewardTrackStepFilters.Where(f => stepIds.Contains(f.RewardTrackTaskStepEntityId))
                .ToListAsync(ct)
                .ConfigureAwait(false)
        );
        db.RewardTrackTaskSteps.RemoveRange(
            await db
                .RewardTrackTaskSteps.Where(s => taskIds.Contains(s.RewardTrackTaskEntityId))
                .ToListAsync(ct)
                .ConfigureAwait(false)
        );
        db.RewardTrackPrizeRewards.RemoveRange(
            await db
                .RewardTrackPrizeRewards.Where(r => prizeIds.Contains(r.RewardTrackPrizeEntityId))
                .ToListAsync(ct)
                .ConfigureAwait(false)
        );
        db.RewardTrackTasks.RemoveRange(tasks);
        db.RewardTrackPrizes.RemoveRange(prizes);
        db.RewardTracks.Remove(row);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await reloader.ReloadAsync(IRewardTrackCatalog.CacheName, ct).ConfigureAwait(false);

        logger.LogWarning("Deleted reward track {TrackId} and all its content.", row.TrackId);

        return RewardTrackAdminResult.Ok(trackRowId);
    }

    public async Task<RewardTrackAdminResult> UpsertTaskAsync(
        int trackRowId,
        RewardTrackTaskSpec spec,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(spec.TaskId) || string.IsNullOrWhiteSpace(spec.ActionCode))
        {
            return RewardTrackAdminResult.Fail("task_id_and_action_required");
        }

        // A condition that can never be true is worse than no condition: the task simply stops
        // advancing, with nothing on screen to say why. Refuse it here, where the operator is
        // still looking at the form.
        if (RewardTrackSequenceRules.FirstProblem(spec.Steps, vocabulary) is string problem)
        {
            return RewardTrackAdminResult.Fail(problem);
        }

        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        RewardTrackEntity? track = await db
            .RewardTracks.FirstOrDefaultAsync(t => t.Id == trackRowId && t.DeletedAt == null, ct)
            .ConfigureAwait(false);

        if (track is null)
        {
            return RewardTrackAdminResult.Fail("track_not_found");
        }

        RewardTrackTaskEntity? row = await db
            .RewardTrackTasks.FirstOrDefaultAsync(
                t =>
                    t.RewardTrackEntityId == trackRowId
                    && t.TaskId == spec.TaskId
                    && t.DeletedAt == null,
                ct
            )
            .ConfigureAwait(false);

        if (row is null)
        {
            row = new RewardTrackTaskEntity
            {
                RewardTrackEntityId = trackRowId,
                TaskId = spec.TaskId,
                ActionCode = spec.ActionCode,
            };

            db.RewardTrackTasks.Add(row);
        }

        row.ActionCode = spec.ActionCode;
        row.Parameter = spec.Parameter;
        row.Mode = spec.Mode;
        row.Premium = spec.Premium;
        row.SortOrder = spec.SortOrder;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // The whole ladder is replaced. Stages carry no player state of their own -- what a player
        // has been paid is a watermark on their own row -- so rewriting them is safe, and it saves
        // an operator a stage-by-stage dance to reshape a task.
        db.RewardTrackTaskLevels.RemoveRange(
            await db
                .RewardTrackTaskLevels.Where(l => l.RewardTrackTaskEntityId == row.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false)
        );

        int index = 0;

        foreach (RewardTrackTaskLevelSpec level in spec.Levels.OrderBy(l => l.RequiredCount))
        {
            db.RewardTrackTaskLevels.Add(
                new RewardTrackTaskLevelEntity
                {
                    RewardTrackTaskEntityId = row.Id,
                    LevelIndex = index++,
                    RequiredCount = level.RequiredCount,
                    PointsReward = level.PointsReward,
                    Premium = level.Premium,
                }
            );
        }

        await ReplaceStepsAsync(db, row, spec.Steps, ct).ConfigureAwait(false);

        track.ContentVersion++;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAndNotifyAsync(track.TrackId, ct).ConfigureAwait(false);

        return RewardTrackAdminResult.Ok(row.Id);
    }

    public async Task<RewardTrackAdminResult> DeleteTaskAsync(int taskRowId, CancellationToken ct)
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        RewardTrackTaskEntity? row = await db
            .RewardTrackTasks.Include(t => t.RewardTrack)
            .FirstOrDefaultAsync(t => t.Id == taskRowId, ct)
            .ConfigureAwait(false);

        if (row is null)
        {
            return RewardTrackAdminResult.Fail("not_found");
        }

        db.RewardTrackTaskLevels.RemoveRange(
            await db
                .RewardTrackTaskLevels.Where(l => l.RewardTrackTaskEntityId == taskRowId)
                .ToListAsync(ct)
                .ConfigureAwait(false)
        );
        await RemoveStepsAsync(db, taskRowId, ct).ConfigureAwait(false);
        db.RewardTrackTasks.Remove(row);

        if (row.RewardTrack is not null)
        {
            row.RewardTrack.ContentVersion++;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAndNotifyAsync(row.RewardTrack?.TrackId, ct).ConfigureAwait(false);

        return RewardTrackAdminResult.Ok(taskRowId);
    }

    public async Task<RewardTrackAdminResult> UpsertPrizeAsync(
        int trackRowId,
        RewardTrackPrizeSpec spec,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(spec.PrizeId))
        {
            return RewardTrackAdminResult.Fail("prize_id_required");
        }

        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        RewardTrackEntity? track = await db
            .RewardTracks.FirstOrDefaultAsync(t => t.Id == trackRowId && t.DeletedAt == null, ct)
            .ConfigureAwait(false);

        if (track is null)
        {
            return RewardTrackAdminResult.Fail("track_not_found");
        }

        RewardTrackPrizeEntity? row = await db
            .RewardTrackPrizes.FirstOrDefaultAsync(
                p =>
                    p.RewardTrackEntityId == trackRowId
                    && p.PrizeId == spec.PrizeId
                    && p.DeletedAt == null,
                ct
            )
            .ConfigureAwait(false);

        if (row is null)
        {
            row = new RewardTrackPrizeEntity
            {
                RewardTrackEntityId = trackRowId,
                PrizeId = spec.PrizeId,
                RequiredPoints = spec.RequiredPoints,
            };

            db.RewardTrackPrizes.Add(row);
        }

        row.RequiredPoints = spec.RequiredPoints;
        row.Premium = spec.Premium;
        row.SortOrder = spec.SortOrder;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // The bundle is replaced wholesale. Claims record the prize id and a rendered summary of
        // what was actually handed over, so an old claim keeps its meaning even after the prize is
        // rewritten -- which is the whole reason that summary column exists.
        db.RewardTrackPrizeRewards.RemoveRange(
            await db
                .RewardTrackPrizeRewards.Where(r => r.RewardTrackPrizeEntityId == row.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false)
        );

        foreach (RewardTrackRewardSpec reward in spec.Rewards)
        {
            db.RewardTrackPrizeRewards.Add(
                new RewardTrackPrizeRewardEntity
                {
                    RewardTrackPrizeEntityId = row.Id,
                    Kind = reward.Kind,
                    RewardTypeId = reward.RewardTypeId,
                    Amount = reward.Amount,
                    ExtraParams = reward.ExtraParams,
                    SortOrder = reward.SortOrder,
                }
            );
        }

        track.ContentVersion++;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAndNotifyAsync(track.TrackId, ct).ConfigureAwait(false);

        return RewardTrackAdminResult.Ok(row.Id);
    }

    public async Task<RewardTrackAdminResult> DeletePrizeAsync(int prizeRowId, CancellationToken ct)
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        RewardTrackPrizeEntity? row = await db
            .RewardTrackPrizes.Include(p => p.RewardTrack)
            .FirstOrDefaultAsync(p => p.Id == prizeRowId, ct)
            .ConfigureAwait(false);

        if (row is null)
        {
            return RewardTrackAdminResult.Fail("not_found");
        }

        db.RewardTrackPrizeRewards.RemoveRange(
            await db
                .RewardTrackPrizeRewards.Where(r => r.RewardTrackPrizeEntityId == prizeRowId)
                .ToListAsync(ct)
                .ConfigureAwait(false)
        );
        db.RewardTrackPrizes.Remove(row);

        if (row.RewardTrack is not null)
        {
            row.RewardTrack.ContentVersion++;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAndNotifyAsync(row.RewardTrack?.TrackId, ct).ConfigureAwait(false);

        return RewardTrackAdminResult.Ok(prizeRowId);
    }

    public async Task<IReadOnlyList<RewardTrackStats>> GetStatsAsync(CancellationToken ct)
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        // Three grouped queries for the whole list rather than three per track.
        Dictionary<string, (int Participants, int Completions, int Premium)> byTrack = await db
            .PlayerRewardTracks.Where(t => t.DeletedAt == null)
            .GroupBy(t => t.TrackId)
            .Select(g => new
            {
                TrackId = g.Key,
                Participants = g.Count(),
                Completions = g.Count(x => x.CompletedAt != null),
                Premium = g.Count(x => x.PremiumUnlocked),
            })
            .ToDictionaryAsync(x => x.TrackId, x => (x.Participants, x.Completions, x.Premium), ct)
            .ConfigureAwait(false);

        Dictionary<string, int> claimsByTrack = await db
            .PlayerRewardTrackClaims.Where(c => c.DeletedAt == null)
            .GroupBy(c => c.TrackId)
            .Select(g => new { TrackId = g.Key, Claims = g.Count() })
            .ToDictionaryAsync(x => x.TrackId, x => x.Claims, ct)
            .ConfigureAwait(false);

        List<RewardTrackStats> stats = [];

        foreach (RewardTrackDefinitionSnapshot track in catalog.Tracks)
        {
            (int participants, int completions, int premium) = byTrack.GetValueOrDefault(
                track.TrackId
            );

            stats.Add(
                new RewardTrackStats(
                    track.TrackId,
                    track.Status,
                    track.Tasks.Length,
                    track.Prizes.Length,
                    participants,
                    completions,
                    premium,
                    claimsByTrack.GetValueOrDefault(track.TrackId)
                )
            );
        }

        return stats;
    }

    public async Task<IReadOnlyList<PlayerRewardTrackAdminRow>> GetPlayerProgressAsync(
        int playerId,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        List<PlayerRewardTrackEntity> rows = await db
            .PlayerRewardTracks.AsNoTracking()
            .Where(t => t.PlayerEntityId == playerId && t.DeletedAt == null)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        Dictionary<string, int> tasks = await db
            .PlayerRewardTrackTasks.Where(t => t.PlayerEntityId == playerId && t.DeletedAt == null)
            .GroupBy(t => t.TrackId)
            .Select(g => new { TrackId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TrackId, x => x.Count, ct)
            .ConfigureAwait(false);

        Dictionary<string, int> claims = await db
            .PlayerRewardTrackClaims.Where(c => c.PlayerEntityId == playerId && c.DeletedAt == null)
            .GroupBy(c => c.TrackId)
            .Select(g => new { TrackId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TrackId, x => x.Count, ct)
            .ConfigureAwait(false);

        return
        [
            .. rows.Select(r => new PlayerRewardTrackAdminRow(
                r.TrackId,
                r.Points,
                r.PremiumUnlocked,
                r.PremiumUnlockedAt,
                r.CompletedAt,
                tasks.GetValueOrDefault(r.TrackId),
                claims.GetValueOrDefault(r.TrackId)
            )),
        ];
    }

    public async Task<RewardTrackAdminResult> ResetPlayerTrackAsync(
        int playerId,
        string trackId,
        CancellationToken ct
    )
    {
        // Through the grain: it caches the player's whole state, so rows deleted behind its back
        // would come straight back on its next write.
        bool reset = await grainFactory
            .GetPlayerRewardTrackGrain(playerId)
            .ResetTrackAsync(trackId, ct)
            .ConfigureAwait(false);

        return reset
            ? RewardTrackAdminResult.Ok(playerId)
            : RewardTrackAdminResult.Fail("no_progress");
    }

    public async Task<RewardTrackAdminResult> GrantPremiumAsync(
        int playerId,
        string trackId,
        CancellationToken ct
    )
    {
        bool granted = await grainFactory
            .GetPlayerRewardTrackGrain(playerId)
            .GrantPremiumAsync(trackId, ct)
            .ConfigureAwait(false);

        return granted
            ? RewardTrackAdminResult.Ok(playerId)
            : RewardTrackAdminResult.Fail("already_premium_or_unknown_track");
    }

    public Task<RewardTrackContentReport> ValidateAsync(CancellationToken ct) =>
        Task.FromResult(RewardTrackContentValidator.Validate(catalog.Tracks));

    private static void Apply(RewardTrackEntity row, RewardTrackSpec spec)
    {
        row.Theme = spec.Theme;
        row.SortOrder = spec.SortOrder;
        row.StartsAt = spec.StartsAt;
        row.ProgressEndsAt = spec.ProgressEndsAt;
        row.ClaimEndsAt = spec.ClaimEndsAt;
        row.UnlockKind = spec.UnlockKind;
        row.UnlockValue = spec.UnlockValue;
        row.CompletionPolicy = spec.CompletionPolicy;
        row.PremiumEnabled = spec.PremiumEnabled;
        row.PremiumBoostPerMille = spec.PremiumBoostPerMille;
        row.PremiumInstantPoints = spec.PremiumInstantPoints;
        row.PremiumCostCredits = spec.PremiumCostCredits;
        row.PremiumCostDiamonds = spec.PremiumCostDiamonds;
        row.Hidden = spec.Hidden;
        row.CampaignCode = spec.CampaignCode;
        row.Status = spec.Status;
    }

    /// <summary>
    /// Rewrites a task's whole sequence, and keeps the task's own action equal to the first step's.
    /// </summary>
    /// <remarks>
    /// That last part is not tidiness. The client draws a task's icon from
    /// <c>reward_track_tasks_&lt;action_code&gt;</c> and there is one action per task on the wire, so
    /// a sequence whose picture disagreed with the action it starts on would be a lie no one can see
    /// from the editor.
    /// </remarks>
    private static async Task ReplaceStepsAsync(
        VortexDbContext db,
        RewardTrackTaskEntity task,
        IReadOnlyList<RewardTrackTaskStepSpec>? steps,
        CancellationToken ct
    )
    {
        await RemoveStepsAsync(db, task.Id, ct).ConfigureAwait(false);

        if (steps is null || steps.Count == 0)
        {
            // A plain task: the catalog builds its single step from the action above.
            return;
        }

        task.ActionCode = steps[0].ActionCode;

        for (int index = 0; index < steps.Count; index++)
        {
            RewardTrackTaskStepEntity step = new()
            {
                RewardTrackTaskEntityId = task.Id,
                StepIndex = index,
                ActionCode = steps[index].ActionCode,
            };

            db.RewardTrackTaskSteps.Add(step);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            // The arranged conditions win over the listed ones, the same way the engine reads them.
            // A caller that sends only a list means what it has always meant, and takes the path it
            // has always taken: rows with no parent and no kind.
            if (steps[index].Tree is { Count: > 0 } tree)
            {
                await WriteNodesAsync(db, step.Id, null, tree, ct).ConfigureAwait(false);
                continue;
            }

            int order = 0;

            foreach (RewardTrackStepFilterSpec filter in steps[index].Filters ?? [])
            {
                db.RewardTrackStepFilters.Add(
                    new RewardTrackStepFilterEntity
                    {
                        RewardTrackTaskStepEntityId = step.Id,
                        SortOrder = order++,
                        FactKey = filter.FactKey.Trim(),
                        Operator = filter.Operator,
                        Value = filter.Value.Trim(),
                    }
                );
            }
        }
    }

    /// <summary>
    /// Writes one level of a step's condition tree, and everything under it.
    /// </summary>
    /// <remarks>
    /// One save per group rather than one per row: a child needs its parent's id and nothing has
    /// one until it is written, but a test is nobody's parent. A step's conditions are a handful of
    /// rows and this runs when an operator presses save, so the extra round trips cost nothing.
    /// </remarks>
    private static async Task WriteNodesAsync(
        VortexDbContext db,
        int stepRowId,
        int? parentId,
        IReadOnlyList<RewardTrackStepNodeSpec> nodes,
        CancellationToken ct
    )
    {
        int order = 0;

        foreach (RewardTrackStepNodeSpec node in nodes)
        {
            RewardTrackStepFilterEntity row = new()
            {
                RewardTrackTaskStepEntityId = stepRowId,
                Kind = node.Kind,
                ParentId = parentId,
                SortOrder = order++,
                FactKey = node.Test?.FactKey.Trim() ?? string.Empty,
                Operator = node.Test?.Operator ?? StepFilterOperator.Equals,
                Value = node.Test?.Value.Trim() ?? string.Empty,
            };

            db.RewardTrackStepFilters.Add(row);

            if (node.Kind == PredicateKind.Test)
            {
                continue;
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await WriteNodesAsync(db, stepRowId, row.Id, node.Children ?? [], ct)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Drops a task's steps and their filters. Neither holds player state.</summary>
    private static async Task RemoveStepsAsync(
        VortexDbContext db,
        int taskRowId,
        CancellationToken ct
    )
    {
        List<int> stepIds =
        [
            .. await db
                .RewardTrackTaskSteps.Where(s => s.RewardTrackTaskEntityId == taskRowId)
                .Select(s => s.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false),
        ];

        if (stepIds.Count == 0)
        {
            return;
        }

        db.RewardTrackStepFilters.RemoveRange(
            await db
                .RewardTrackStepFilters.Where(f => stepIds.Contains(f.RewardTrackTaskStepEntityId))
                .ToListAsync(ct)
                .ConfigureAwait(false)
        );
        db.RewardTrackTaskSteps.RemoveRange(
            await db
                .RewardTrackTaskSteps.Where(s => s.RewardTrackTaskEntityId == taskRowId)
                .ToListAsync(ct)
                .ConfigureAwait(false)
        );

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static async Task CloneStepsAsync(
        VortexDbContext db,
        int sourceTaskRowId,
        int targetTaskRowId,
        CancellationToken ct
    )
    {
        foreach (
            RewardTrackTaskStepEntity source in await db
                .RewardTrackTaskSteps.AsNoTracking()
                .Where(s => s.RewardTrackTaskEntityId == sourceTaskRowId && s.DeletedAt == null)
                .OrderBy(s => s.StepIndex)
                .ToListAsync(ct)
                .ConfigureAwait(false)
        )
        {
            RewardTrackTaskStepEntity clone = new()
            {
                RewardTrackTaskEntityId = targetTaskRowId,
                StepIndex = source.StepIndex,
                ActionCode = source.ActionCode,
                Parameter = source.Parameter,
            };

            db.RewardTrackTaskSteps.Add(clone);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            // A copied row cannot keep its parent's id: the copy of that parent is a different row.
            // Ordered by id so a parent is always copied before its children, which is the order
            // they were written in, and mapped as we go.
            Dictionary<int, int> copied = [];

            foreach (
                RewardTrackStepFilterEntity filter in await db
                    .RewardTrackStepFilters.AsNoTracking()
                    .Where(f => f.RewardTrackTaskStepEntityId == source.Id && f.DeletedAt == null)
                    .OrderBy(f => f.Id)
                    .ToListAsync(ct)
                    .ConfigureAwait(false)
            )
            {
                RewardTrackStepFilterEntity copy = new()
                {
                    RewardTrackTaskStepEntityId = clone.Id,
                    Kind = filter.Kind,
                    // A parent that is not in the map is one this step never had. Copying the row
                    // under the root keeps it where an operator can see and delete it, which is
                    // better than a row that lands nowhere and is silently never read.
                    ParentId =
                        filter.ParentId is int parent && copied.TryGetValue(parent, out int mapped)
                            ? mapped
                            : null,
                    SortOrder = filter.SortOrder,
                    FactKey = filter.FactKey,
                    Operator = filter.Operator,
                    Value = filter.Value,
                };

                db.RewardTrackStepFilters.Add(copy);

                if (filter.Kind == PredicateKind.Test)
                {
                    // Nobody's parent, so nobody needs its id.
                    continue;
                }

                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                copied[filter.Id] = copy.Id;
            }
        }
    }

    private static async Task CloneTasksAsync(
        VortexDbContext db,
        int sourceTrackRowId,
        int targetTrackRowId,
        CancellationToken ct
    )
    {
        List<RewardTrackTaskEntity> tasks = await db
            .RewardTrackTasks.AsNoTracking()
            .Where(t => t.RewardTrackEntityId == sourceTrackRowId && t.DeletedAt == null)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (RewardTrackTaskEntity task in tasks)
        {
            RewardTrackTaskEntity clone = new()
            {
                RewardTrackEntityId = targetTrackRowId,
                TaskId = task.TaskId,
                ActionCode = task.ActionCode,
                Parameter = task.Parameter,
                Mode = task.Mode,
                Premium = task.Premium,
                SortOrder = task.SortOrder,
            };

            db.RewardTrackTasks.Add(clone);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            foreach (
                RewardTrackTaskLevelEntity level in await db
                    .RewardTrackTaskLevels.AsNoTracking()
                    .Where(l => l.RewardTrackTaskEntityId == task.Id && l.DeletedAt == null)
                    .ToListAsync(ct)
                    .ConfigureAwait(false)
            )
            {
                db.RewardTrackTaskLevels.Add(
                    new RewardTrackTaskLevelEntity
                    {
                        RewardTrackTaskEntityId = clone.Id,
                        LevelIndex = level.LevelIndex,
                        RequiredCount = level.RequiredCount,
                        PointsReward = level.PointsReward,
                        Premium = level.Premium,
                    }
                );
            }

            // The sequence travels with the task, like its stages. A clone that dropped it would
            // count the opening action on its own -- a wider task, quietly, in a copy made to be
            // the same.
            await CloneStepsAsync(db, task.Id, clone.Id, ct).ConfigureAwait(false);
        }
    }

    private static async Task ClonePrizesAsync(
        VortexDbContext db,
        int sourceTrackRowId,
        int targetTrackRowId,
        CancellationToken ct
    )
    {
        List<RewardTrackPrizeEntity> prizes = await db
            .RewardTrackPrizes.AsNoTracking()
            .Where(p => p.RewardTrackEntityId == sourceTrackRowId && p.DeletedAt == null)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (RewardTrackPrizeEntity prize in prizes)
        {
            RewardTrackPrizeEntity clone = new()
            {
                RewardTrackEntityId = targetTrackRowId,
                PrizeId = prize.PrizeId,
                RequiredPoints = prize.RequiredPoints,
                Premium = prize.Premium,
                SortOrder = prize.SortOrder,
            };

            db.RewardTrackPrizes.Add(clone);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            foreach (
                RewardTrackPrizeRewardEntity reward in await db
                    .RewardTrackPrizeRewards.AsNoTracking()
                    .Where(r => r.RewardTrackPrizeEntityId == prize.Id && r.DeletedAt == null)
                    .ToListAsync(ct)
                    .ConfigureAwait(false)
            )
            {
                db.RewardTrackPrizeRewards.Add(
                    new RewardTrackPrizeRewardEntity
                    {
                        RewardTrackPrizeEntityId = clone.Id,
                        Kind = reward.Kind,
                        RewardTypeId = reward.RewardTypeId,
                        Amount = reward.Amount,
                        ExtraParams = reward.ExtraParams,
                        SortOrder = reward.SortOrder,
                    }
                );
            }
        }
    }

    /// <summary>
    /// Reloads the catalog and tells anyone looking at the track that it changed.
    /// </summary>
    /// <remarks>
    /// Only the players who already have a row on it are notified — which is who is affected, and
    /// who the database can name without walking every online session.
    /// </remarks>
    private async Task ReloadAndNotifyAsync(string? trackId, CancellationToken ct)
    {
        await reloader.ReloadAsync(IRewardTrackCatalog.CacheName, ct).ConfigureAwait(false);

        if (string.IsNullOrEmpty(trackId))
        {
            return;
        }

        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        List<int> playerIds = await db
            .PlayerRewardTracks.Where(t => t.TrackId == trackId && t.DeletedAt == null)
            .Select(t => t.PlayerEntityId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // Concurrently: these are independent grains, and a campaign with a few thousand
        // participants would otherwise take a few thousand sequential round trips to notify.
        await Task.WhenAll(
                playerIds.Select(id =>
                    grainFactory.GetPlayerRewardTrackGrain(id).InvalidateAsync(ct)
                )
            )
            .ConfigureAwait(false);
    }
}
