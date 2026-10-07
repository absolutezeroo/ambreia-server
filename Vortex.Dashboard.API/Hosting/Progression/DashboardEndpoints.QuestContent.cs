using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Vortex.Dashboard.API.Api;
using Vortex.Dashboard.API.Api.Progression;
using Vortex.Dashboard.API.Api.Progression.Contracts;
using Vortex.Dashboard.API.Operations;
using Vortex.Dashboard.API.Operations.Progression;
using Vortex.Dashboard.API.Operations.Progression.Contracts;
using Vortex.Primitives.Permissions;

namespace Vortex.Dashboard.API.Hosting;

/// <summary>
/// Daily tasks: the content the quest system runs on that is not itself a quest.
/// Deliberately under the existing quest capability rather than a new one — it is the same domain
/// and the same operators, and a capability nobody has been granted is a page nobody can open.
/// </summary>
internal static partial class DashboardEndpoints
{
    private const string TagQuestContent = "Quests";
    private const string ApiDailyTasks = ApiV1 + "/daily-tasks";

    public static void MapQuestContentReads(WebApplication app)
    {
        MapReadGet<DailyTaskList>(
            app,
            ApiDailyTasks,
            (QuestContentReads content, CancellationToken ct) =>
                OkAsync(content.DailyTasksAsync(ct)),
            Capabilities.Dashboard.QuestsRead,
            TagQuestContent
        );
    }

    public static void MapQuestContentOperations(WebApplication app)
    {
        MapPost(
            app,
            ApiOperations + "/daily-tasks",
            async (
                HttpContext ctx,
                CreateDailyTaskRequest body,
                QuestContentOperations ops,
                CancellationToken ct
            ) =>
                string.IsNullOrWhiteSpace(body.TaskCode)
                || string.IsNullOrWhiteSpace(body.QuestTypeCode)
                    ? Results.BadRequest(new { error = "invalid_request" })
                    : Results.Ok(
                        await ops.CreateDailyTaskAsync(body, ctx.ActorEmail(), ct)
                            .ConfigureAwait(false)
                    ),
            Capabilities.Dashboard.OpsQuestsManage,
            TagQuestContent
        );
        MapPost(
            app,
            ApiOperations + "/daily-tasks/update",
            async (
                HttpContext ctx,
                UpdateDailyTaskRequest body,
                QuestContentOperations ops,
                CancellationToken ct
            ) =>
                body.TaskId <= 0
                || string.IsNullOrWhiteSpace(body.TaskCode)
                || string.IsNullOrWhiteSpace(body.QuestTypeCode)
                    ? Results.BadRequest(new { error = "invalid_request" })
                    : Results.Ok(
                        await ops.UpdateDailyTaskAsync(body, ctx.ActorEmail(), ct)
                            .ConfigureAwait(false)
                    ),
            Capabilities.Dashboard.OpsQuestsManage,
            TagQuestContent
        );
        MapPost(
            app,
            ApiOperations + "/daily-tasks/delete",
            async (
                HttpContext ctx,
                DeleteDailyTaskRequest body,
                QuestContentOperations ops,
                CancellationToken ct
            ) =>
                body.TaskId <= 0
                    ? Results.BadRequest(new { error = "invalid_request" })
                    : Results.Ok(
                        await ops.DeleteDailyTaskAsync(body, ctx.ActorEmail(), ct)
                            .ConfigureAwait(false)
                    ),
            Capabilities.Dashboard.OpsQuestsManage,
            TagQuestContent
        );
    }
}
