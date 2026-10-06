using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Vortex.Dashboard.API.Api;
using Vortex.Dashboard.API.Api.Hotel;
using Vortex.Dashboard.API.Api.Hotel.Contracts;
using Vortex.Dashboard.API.Operations;
using Vortex.Dashboard.API.Operations.Hotel;
using Vortex.Dashboard.API.Operations.Hotel.Contracts;
using Vortex.Primitives.Permissions;

namespace Vortex.Dashboard.API.Hosting;

/// <summary>
/// Navigator configuration: one read returning the whole configuration (tabs, their blocks, the
/// room categories and the known search codes), and the CRUD that edits it. Every write
/// reloads the live navigator snapshot — see <c>INavigatorAdminService</c>.
/// </summary>
internal static partial class DashboardEndpoints
{
    private const string TagNavigator = "Navigator";
    private const string ApiNavigator = ApiV1 + "/navigator";
    private const string OpsNavigator = ApiOperations + "/navigator";

    public static void MapNavigatorReads(WebApplication app)
    {
        MapReadGet<NavigatorSetup>(
            app,
            ApiNavigator + "/config",
            (NavigatorReads reads, CancellationToken ct) => OkAsync(reads.NavigatorConfigAsync(ct)),
            Capabilities.Dashboard.NavigatorRead,
            TagNavigator
        );
    }

    public static void MapNavigatorOperations(WebApplication app)
    {
        MapPost(
            app,
            OpsNavigator + "/contexts",
            async (
                HttpContext ctx,
                CreateNavigatorContextRequest body,
                NavigatorOperations ops,
                CancellationToken ct
            ) =>
                string.IsNullOrWhiteSpace(body.SearchCode)
                    ? Results.BadRequest(new { error = "invalid_request" })
                    : Results.Ok(
                        await ops.CreateNavigatorContextAsync(body, ctx.ActorEmail(), ct)
                            .ConfigureAwait(false)
                    ),
            Capabilities.Dashboard.OpsNavigatorManage,
            TagNavigator
        );
        MapPost(
            app,
            OpsNavigator + "/contexts/update",
            async (
                HttpContext ctx,
                UpdateNavigatorContextRequest body,
                NavigatorOperations ops,
                CancellationToken ct
            ) =>
                body.ContextId <= 0 || string.IsNullOrWhiteSpace(body.SearchCode)
                    ? Results.BadRequest(new { error = "invalid_request" })
                    : Results.Ok(
                        await ops.UpdateNavigatorContextAsync(body, ctx.ActorEmail(), ct)
                            .ConfigureAwait(false)
                    ),
            Capabilities.Dashboard.OpsNavigatorManage,
            TagNavigator
        );
        MapPost(
            app,
            OpsNavigator + "/contexts/delete",
            async (
                HttpContext ctx,
                DeleteNavigatorContextRequest body,
                NavigatorOperations ops,
                CancellationToken ct
            ) =>
                body.ContextId <= 0
                    ? Results.BadRequest(new { error = "invalid_request" })
                    : Results.Ok(
                        await ops.DeleteNavigatorContextAsync(body, ctx.ActorEmail(), ct)
                            .ConfigureAwait(false)
                    ),
            Capabilities.Dashboard.OpsNavigatorManage,
            TagNavigator
        );
        MapPost(
            app,
            OpsNavigator + "/quick-links",
            async (
                HttpContext ctx,
                CreateNavigatorQuickLinkRequest body,
                NavigatorOperations ops,
                CancellationToken ct
            ) =>
                body.ContextId <= 0 || string.IsNullOrWhiteSpace(body.SearchCode)
                    ? Results.BadRequest(new { error = "invalid_request" })
                    : Results.Ok(
                        await ops.CreateNavigatorQuickLinkAsync(body, ctx.ActorEmail(), ct)
                            .ConfigureAwait(false)
                    ),
            Capabilities.Dashboard.OpsNavigatorManage,
            TagNavigator
        );
        MapPost(
            app,
            OpsNavigator + "/quick-links/update",
            async (
                HttpContext ctx,
                UpdateNavigatorQuickLinkRequest body,
                NavigatorOperations ops,
                CancellationToken ct
            ) =>
                body.QuickLinkId <= 0
                || body.ContextId <= 0
                || string.IsNullOrWhiteSpace(body.SearchCode)
                    ? Results.BadRequest(new { error = "invalid_request" })
                    : Results.Ok(
                        await ops.UpdateNavigatorQuickLinkAsync(body, ctx.ActorEmail(), ct)
                            .ConfigureAwait(false)
                    ),
            Capabilities.Dashboard.OpsNavigatorManage,
            TagNavigator
        );
        MapPost(
            app,
            OpsNavigator + "/quick-links/delete",
            async (
                HttpContext ctx,
                DeleteNavigatorQuickLinkRequest body,
                NavigatorOperations ops,
                CancellationToken ct
            ) =>
                body.QuickLinkId <= 0
                    ? Results.BadRequest(new { error = "invalid_request" })
                    : Results.Ok(
                        await ops.DeleteNavigatorQuickLinkAsync(body, ctx.ActorEmail(), ct)
                            .ConfigureAwait(false)
                    ),
            Capabilities.Dashboard.OpsNavigatorManage,
            TagNavigator
        );
        MapPost(
            app,
            OpsNavigator + "/categories",
            async (
                HttpContext ctx,
                CreateNavigatorFlatCategoryRequest body,
                NavigatorOperations ops,
                CancellationToken ct
            ) =>
                string.IsNullOrWhiteSpace(body.Name)
                    ? Results.BadRequest(new { error = "invalid_request" })
                    : Results.Ok(
                        await ops.CreateNavigatorFlatCategoryAsync(body, ctx.ActorEmail(), ct)
                            .ConfigureAwait(false)
                    ),
            Capabilities.Dashboard.OpsNavigatorManage,
            TagNavigator
        );
        MapPost(
            app,
            OpsNavigator + "/categories/update",
            async (
                HttpContext ctx,
                UpdateNavigatorFlatCategoryRequest body,
                NavigatorOperations ops,
                CancellationToken ct
            ) =>
                body.CategoryId <= 0 || string.IsNullOrWhiteSpace(body.Name)
                    ? Results.BadRequest(new { error = "invalid_request" })
                    : Results.Ok(
                        await ops.UpdateNavigatorFlatCategoryAsync(body, ctx.ActorEmail(), ct)
                            .ConfigureAwait(false)
                    ),
            Capabilities.Dashboard.OpsNavigatorManage,
            TagNavigator
        );
        MapPost(
            app,
            OpsNavigator + "/categories/delete",
            async (
                HttpContext ctx,
                DeleteNavigatorFlatCategoryRequest body,
                NavigatorOperations ops,
                CancellationToken ct
            ) =>
                body.CategoryId <= 0
                    ? Results.BadRequest(new { error = "invalid_request" })
                    : Results.Ok(
                        await ops.DeleteNavigatorFlatCategoryAsync(body, ctx.ActorEmail(), ct)
                            .ConfigureAwait(false)
                    ),
            Capabilities.Dashboard.OpsNavigatorManage,
            TagNavigator
        );
        MapPost(
            app,
            OpsNavigator + "/seed-defaults",
            async (
                HttpContext ctx,
                SeedNavigatorDefaultsRequest body,
                NavigatorOperations ops,
                CancellationToken ct
            ) =>
                Results.Ok(
                    await ops.SeedNavigatorDefaultsAsync(body, ctx.ActorEmail(), ct)
                        .ConfigureAwait(false)
                ),
            Capabilities.Dashboard.OpsNavigatorManage,
            TagNavigator
        );
    }
}
