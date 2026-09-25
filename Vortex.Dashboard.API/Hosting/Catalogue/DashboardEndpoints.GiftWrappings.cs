using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Vortex.Dashboard.API.Api;
using Vortex.Dashboard.API.Api.Catalogue;
using Vortex.Dashboard.API.Api.Catalogue.Contracts;
using Vortex.Dashboard.API.Operations;
using Vortex.Dashboard.API.Operations.Catalogue;
using Vortex.Dashboard.API.Operations.Catalogue.Contracts;
using Vortex.Primitives.Permissions;

namespace Vortex.Dashboard.API.Hosting;

/// <summary>
/// The gift dialog's parcels, boxes and ribbons. Until this existed they were constants in the
/// emulator; every write reloads the live list, so a change is in the next dialog a player opens.
/// </summary>
internal static partial class DashboardEndpoints
{
    private const string TagGiftWrappings = "GiftWrappings";
    private const string ApiGiftWrappings = ApiV1 + "/gift-wrappings";

    public static void MapGiftWrappingReads(WebApplication app) =>
        MapReadGet<GiftWrappingList>(
            app,
            ApiGiftWrappings,
            (GiftWrappingReads reads, CancellationToken ct) =>
                OkAsync(reads.GiftWrappingsAsync(ct)),
            Capabilities.Dashboard.GiftWrappingsRead,
            TagGiftWrappings
        );

    public static void MapGiftWrappingOperations(WebApplication app)
    {
        MapPost(
            app,
            ApiOperations + "/gift-wrappings",
            async (
                HttpContext ctx,
                CreateGiftWrappingRequest body,
                GiftWrappingOperations ops,
                CancellationToken ct
            ) =>
                string.IsNullOrWhiteSpace(body.Kind)
                    ? Results.BadRequest(new { error = "invalid_request" })
                    : Results.Ok(
                        await ops.CreateGiftWrappingAsync(body, ctx.ActorEmail(), ct)
                            .ConfigureAwait(false)
                    ),
            Capabilities.Dashboard.OpsGiftWrappingsManage,
            TagGiftWrappings
        );
        MapPost(
            app,
            ApiOperations + "/gift-wrappings/update",
            async (
                HttpContext ctx,
                UpdateGiftWrappingRequest body,
                GiftWrappingOperations ops,
                CancellationToken ct
            ) =>
                body.WrappingId <= 0 || string.IsNullOrWhiteSpace(body.Kind)
                    ? Results.BadRequest(new { error = "invalid_request" })
                    : Results.Ok(
                        await ops.UpdateGiftWrappingAsync(body, ctx.ActorEmail(), ct)
                            .ConfigureAwait(false)
                    ),
            Capabilities.Dashboard.OpsGiftWrappingsManage,
            TagGiftWrappings
        );
        MapPost(
            app,
            ApiOperations + "/gift-wrappings/delete",
            async (
                HttpContext ctx,
                DeleteGiftWrappingRequest body,
                GiftWrappingOperations ops,
                CancellationToken ct
            ) =>
                body.WrappingId <= 0
                    ? Results.BadRequest(new { error = "invalid_request" })
                    : Results.Ok(
                        await ops.DeleteGiftWrappingAsync(body, ctx.ActorEmail(), ct)
                            .ConfigureAwait(false)
                    ),
            Capabilities.Dashboard.OpsGiftWrappingsManage,
            TagGiftWrappings
        );
        MapPost(
            app,
            ApiOperations + "/gift-wrappings/reload",
            async (
                HttpContext ctx,
                ReloadGiftWrappingsRequest body,
                GiftWrappingOperations ops,
                CancellationToken ct
            ) =>
                Results.Ok(
                    await ops.ReloadGiftWrappingsAsync(body, ctx.ActorEmail(), ct)
                        .ConfigureAwait(false)
                ),
            Capabilities.Dashboard.OpsGiftWrappingsManage,
            TagGiftWrappings
        );
    }
}
