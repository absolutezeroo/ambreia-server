using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Entities.Achievements;
using Vortex.Primitives.Content;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players.Providers;
using Vortex.Primitives.Players.Wallet;

namespace Vortex.Dashboard.API.Admin.Hotel;

/// <summary>
/// Writes for the content the dashboard reads: achievement ladders here, the
/// hotel's smaller tables in the sibling partial.
/// <para>
/// The achievement ladder is cached by a kept-alive manager grain that loads once and never
/// re-reads, so every accepted write ends with a reload. A failed reload is rethrown, never
/// swallowed: the row is already committed, and a silent stale cache is the "DB write not reflected
/// in live state" bug class called out in AGENTS.md.
/// </para>
/// </summary>
internal sealed partial class ContentAdminService(
    IDbContextFactory<VortexDbContext> dbContextFactory,
    IGrainFactory grainFactory,
    ICurrencyTypeProvider currencyTypes,
    ILogger<ContentAdminService> logger
) : IContentAdminService
{
    public async Task<ContentAdminResult> CreateAchievementAsync(
        AchievementSpec spec,
        CancellationToken ct
    )
    {
        if (ValidateAchievement(spec) is { } error)
        {
            return ContentAdminResult.Fail(error);
        }

        string name = spec.Name.Trim();

        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        if (await db.Achievements.AnyAsync(a => a.Name == name, ct).ConfigureAwait(false))
        {
            // The name builds every badge code and is what triggers look up, so two rows sharing one
            // would hand the same badge to two ladders.
            return ContentAdminResult.Fail("achievement_name_taken");
        }

        AchievementEntity entity = new()
        {
            Name = name,
            Category = spec.Category.Trim(),
            DisplayMethod = spec.DisplayMethod,
        };

        db.Achievements.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAchievementsAsync(ct).ConfigureAwait(false);

        return ContentAdminResult.Ok(entity.Id);
    }

    public async Task<ContentAdminResult> UpdateAchievementAsync(
        int achievementId,
        AchievementSpec spec,
        CancellationToken ct
    )
    {
        if (ValidateAchievement(spec) is { } error)
        {
            return ContentAdminResult.Fail(error);
        }

        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        AchievementEntity? entity = await db
            .Achievements.FirstOrDefaultAsync(a => a.Id == achievementId, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return ContentAdminResult.Fail("achievement_not_found");
        }

        string name = spec.Name.Trim();

        if (
            !string.Equals(entity.Name, name, StringComparison.Ordinal)
            && await db
                .Achievements.AnyAsync(a => a.Name == name && a.Id != achievementId, ct)
                .ConfigureAwait(false)
        )
        {
            return ContentAdminResult.Fail("achievement_name_taken");
        }

        entity.Name = name;
        entity.Category = spec.Category.Trim();
        entity.DisplayMethod = spec.DisplayMethod;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAchievementsAsync(ct).ConfigureAwait(false);

        return ContentAdminResult.Ok(entity.Id);
    }

    public async Task<ContentAdminResult> DeleteAchievementAsync(
        int achievementId,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        AchievementEntity? entity = await db
            .Achievements.FirstOrDefaultAsync(a => a.Id == achievementId, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return ContentAdminResult.Fail("achievement_not_found");
        }

        bool hasProgress = await db
            .PlayerAchievements.AnyAsync(p => p.AchievementEntityId == achievementId, ct)
            .ConfigureAwait(false);

        if (hasProgress)
        {
            // Progress rows carry the FK, and the badges already handed out reference the ladder's
            // codes. Deleting under them would orphan both.
            return ContentAdminResult.Fail("achievement_has_progress");
        }

        List<AchievementLevelEntity> levels = await db
            .AchievementLevels.Where(l => l.AchievementEntityId == achievementId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        db.AchievementLevels.RemoveRange(levels);
        db.Achievements.Remove(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAchievementsAsync(ct).ConfigureAwait(false);

        return ContentAdminResult.Ok(achievementId);
    }

    public async Task<ContentAdminResult> UpsertAchievementLevelAsync(
        int achievementId,
        AchievementLevelSpec spec,
        CancellationToken ct
    )
    {
        if (spec.Level <= 0)
        {
            return ContentAdminResult.Fail("level_must_be_positive");
        }

        if (string.IsNullOrWhiteSpace(spec.BadgeCode))
        {
            return ContentAdminResult.Fail("badge_code_required");
        }

        // A reward in a currency this hotel has no enabled row for is paid by nobody: the wallet
        // grant no-ops and the player just never sees it. Refuse it here, where there is still an
        // operator to tell.
        if (
            CurrencyRewardRules.Validate(currencyTypes, spec.RewardType, spec.RewardAmount) is
            { } rewardError
        )
        {
            return ContentAdminResult.Fail(rewardError);
        }

        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        if (!await db.Achievements.AnyAsync(a => a.Id == achievementId, ct).ConfigureAwait(false))
        {
            return ContentAdminResult.Fail("achievement_not_found");
        }

        AchievementLevelEntity? entity = await db
            .AchievementLevels.FirstOrDefaultAsync(
                l => l.AchievementEntityId == achievementId && l.Level == spec.Level,
                ct
            )
            .ConfigureAwait(false);

        if (entity is null)
        {
            entity = new AchievementLevelEntity
            {
                AchievementEntityId = achievementId,
                Level = spec.Level,
                BadgeCode = spec.BadgeCode.Trim(),
                ProgressRequirement = Math.Max(1, spec.ProgressRequirement),
                RewardAmount = Math.Max(0, spec.RewardAmount),
                RewardType = spec.RewardType,
                ScorePoints = Math.Max(0, spec.ScorePoints),
            };

            db.AchievementLevels.Add(entity);
        }
        else
        {
            entity.BadgeCode = spec.BadgeCode.Trim();
            entity.ProgressRequirement = Math.Max(1, spec.ProgressRequirement);
            entity.RewardAmount = Math.Max(0, spec.RewardAmount);
            entity.RewardType = spec.RewardType;
            entity.ScorePoints = Math.Max(0, spec.ScorePoints);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAchievementsAsync(ct).ConfigureAwait(false);

        return ContentAdminResult.Ok(entity.Id);
    }

    public async Task<ContentAdminResult> DeleteAchievementLevelAsync(
        int levelId,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        AchievementLevelEntity? entity = await db
            .AchievementLevels.FirstOrDefaultAsync(l => l.Id == levelId, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return ContentAdminResult.Fail("level_not_found");
        }

        bool reached = await db
            .PlayerAchievements.AnyAsync(
                p => p.AchievementEntityId == entity.AchievementEntityId && p.Level >= entity.Level,
                ct
            )
            .ConfigureAwait(false);

        if (reached)
        {
            // Someone already holds this rung's badge; removing the row leaves them holding a badge
            // the ladder no longer explains.
            return ContentAdminResult.Fail("level_already_reached");
        }

        db.AchievementLevels.Remove(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAchievementsAsync(ct).ConfigureAwait(false);

        return ContentAdminResult.Ok(levelId);
    }

    private static string? ValidateAchievement(AchievementSpec spec) =>
        string.IsNullOrWhiteSpace(spec.Name) ? "name_required"
        : string.IsNullOrWhiteSpace(spec.Category) ? "category_required"
        : null;

    private async Task ReloadAchievementsAsync(CancellationToken ct)
    {
        try
        {
            await grainFactory.GetAchievementManagerGrain().ReloadAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Achievement definition cache reload failed after an admin write committed -- live achievements are stale until the next reload or restart"
            );
            throw;
        }
    }
}
