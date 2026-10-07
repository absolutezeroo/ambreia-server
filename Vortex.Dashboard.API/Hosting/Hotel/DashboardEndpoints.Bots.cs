using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Vortex.Dashboard.API.Api;
using Vortex.Dashboard.API.Api.Hotel;
using Vortex.Dashboard.API.Api.Hotel.Contracts;
using Vortex.Primitives.Permissions;

namespace Vortex.Dashboard.API.Hosting;

/// <summary>
/// Read-only surface for bots and hand items. Bots are edited from inside the game (the client's own
/// bot menu is the authoring tool), so the dashboard observes rather than writes.
/// </summary>
internal static partial class DashboardEndpoints
{
    private const string TagBots = "Bots";
    private const string ApiBots = ApiV1 + "/bots";

    public static void MapBotReads(WebApplication app)
    {
        MapReadGet<BotListResponse>(
            app,
            ApiBots,
            (HttpContext ctx, BotReads reads, CancellationToken ct) =>
                OkAsync(reads.BotsAsync(ctx.QueryAsNameValues(), ct)),
            Capabilities.Dashboard.BotsRead,
            TagBots
        );
        MapReadGet<BotStats>(
            app,
            ApiBots + "/stats",
            (HttpContext ctx, BotReads reads, CancellationToken ct) =>
                OkAsync(reads.BotsStatsAsync(ctx.QueryAsNameValues(), ct)),
            Capabilities.Dashboard.BotsRead,
            TagBots
        );
        MapReadGetNullable<BotDetail>(
            app,
            ApiBots + "/{botId:int}",
            (int botId, BotReads reads, CancellationToken ct) =>
                OkNullableAsync(reads.BotDetailAsync(botId, ct)),
            Capabilities.Dashboard.BotsRead,
            TagBots
        );
    }
}
