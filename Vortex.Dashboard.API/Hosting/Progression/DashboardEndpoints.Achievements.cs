using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Vortex.Dashboard.API.Api;
using Vortex.Dashboard.API.Api.Progression;
using Vortex.Dashboard.API.Api.Progression.Contracts;
using Vortex.Primitives.Permissions;

namespace Vortex.Dashboard.API.Hosting;

/// <summary>
/// Read-only achievement surface: the definition catalogue with its level ladder, one definition's
/// player distribution, and hotel-wide progression health. Achievement definitions are seeded rather
/// than authored in the dashboard, so there is no operations half here.
/// </summary>
internal static partial class DashboardEndpoints
{
    private const string TagAchievements = "Achievements";
    private const string ApiAchievements = ApiV1 + "/achievements";

    public static void MapAchievementReads(WebApplication app)
    {
        MapReadGet<AchievementListResponse>(
            app,
            ApiAchievements,
            (HttpContext ctx, AchievementReads reads, CancellationToken ct) =>
                OkAsync(reads.AchievementsAsync(ctx.QueryAsNameValues(), ct)),
            Capabilities.Dashboard.AchievementsRead,
            TagAchievements
        );
        MapReadGet<AchievementStats>(
            app,
            ApiAchievements + "/stats",
            (AchievementReads reads, CancellationToken ct) =>
                OkAsync(reads.AchievementsStatsAsync(ct)),
            Capabilities.Dashboard.AchievementsRead,
            TagAchievements
        );
        MapReadGetNullable<AchievementDetail>(
            app,
            ApiAchievements + "/{achievementId:int}",
            (int achievementId, AchievementReads reads, CancellationToken ct) =>
                OkNullableAsync(reads.AchievementDetailAsync(achievementId, ct)),
            Capabilities.Dashboard.AchievementsRead,
            TagAchievements
        );
    }
}
