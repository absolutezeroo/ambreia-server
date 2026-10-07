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

internal static partial class DashboardEndpoints
{
    private const string TagLootTables = "LootTables";
    private const string ApiLootTables = ApiV1 + "/loot-tables";

    public static void MapLootTableReads(WebApplication app)
    {
        MapReadGet<LootTableContent>(
            app,
            ApiLootTables,
            (LootTableReads reads, CancellationToken ct) => OkAsync(reads.LootTablesAsync(ct)),
            Capabilities.Dashboard.LootTablesRead,
            TagLootTables
        );
        MapReadGet<LootTableStats>(
            app,
            ApiLootTables + "/stats",
            (HttpContext ctx, LootTableReads reads, CancellationToken ct) =>
                OkAsync(reads.LootTableStatsAsync(ctx.QueryAsNameValues(), ct)),
            Capabilities.Dashboard.LootTablesRead,
            TagLootTables
        );
    }

    public static void MapLootTableOperations(WebApplication app)
    {
        MapPost(
            app,
            ApiOperations + "/loot-tables",
            async (
                HttpContext ctx,
                CreateLootTableRequest body,
                LootTableOperations ops,
                CancellationToken ct
            ) =>
                string.IsNullOrWhiteSpace(body.Code) || string.IsNullOrWhiteSpace(body.Name)
                    ? Results.BadRequest(new { error = "invalid_request" })
                    : Results.Ok(
                        await ops.CreateLootTableAsync(body, ctx.ActorEmail(), ct)
                            .ConfigureAwait(false)
                    ),
            Capabilities.Dashboard.OpsLootTablesManage,
            TagLootTables
        );
        MapPost(
            app,
            ApiOperations + "/loot-tables/update",
            async (
                HttpContext ctx,
                UpdateLootTableRequest body,
                LootTableOperations ops,
                CancellationToken ct
            ) =>
                body.PoolId <= 0
                || string.IsNullOrWhiteSpace(body.Code)
                || string.IsNullOrWhiteSpace(body.Name)
                    ? Results.BadRequest(new { error = "invalid_request" })
                    : Results.Ok(
                        await ops.UpdateLootTableAsync(body, ctx.ActorEmail(), ct)
                            .ConfigureAwait(false)
                    ),
            Capabilities.Dashboard.OpsLootTablesManage,
            TagLootTables
        );
        MapPost(
            app,
            ApiOperations + "/loot-tables/delete",
            async (
                HttpContext ctx,
                DeleteLootTableRequest body,
                LootTableOperations ops,
                CancellationToken ct
            ) =>
                body.PoolId <= 0
                    ? Results.BadRequest(new { error = "invalid_request" })
                    : Results.Ok(
                        await ops.DeleteLootTableAsync(body, ctx.ActorEmail(), ct)
                            .ConfigureAwait(false)
                    ),
            Capabilities.Dashboard.OpsLootTablesManage,
            TagLootTables
        );
        MapPost(
            app,
            ApiOperations + "/loot-tables/entries",
            async (
                HttpContext ctx,
                CreateLootEntryRequest body,
                LootTableOperations ops,
                CancellationToken ct
            ) =>
                string.IsNullOrWhiteSpace(body.PoolCode)
                || string.IsNullOrWhiteSpace(body.ProductType)
                || body.Weight <= 0
                    ? Results.BadRequest(new { error = "invalid_request" })
                    : Results.Ok(
                        await ops.CreateLootEntryAsync(body, ctx.ActorEmail(), ct)
                            .ConfigureAwait(false)
                    ),
            Capabilities.Dashboard.OpsLootTablesManage,
            TagLootTables
        );
        MapPost(
            app,
            ApiOperations + "/loot-tables/entries/update",
            async (
                HttpContext ctx,
                UpdateLootEntryRequest body,
                LootTableOperations ops,
                CancellationToken ct
            ) =>
                body.EntryId <= 0
                || string.IsNullOrWhiteSpace(body.PoolCode)
                || string.IsNullOrWhiteSpace(body.ProductType)
                || body.Weight <= 0
                    ? Results.BadRequest(new { error = "invalid_request" })
                    : Results.Ok(
                        await ops.UpdateLootEntryAsync(body, ctx.ActorEmail(), ct)
                            .ConfigureAwait(false)
                    ),
            Capabilities.Dashboard.OpsLootTablesManage,
            TagLootTables
        );
        MapPost(
            app,
            ApiOperations + "/loot-tables/entries/delete",
            async (
                HttpContext ctx,
                DeleteLootEntryRequest body,
                LootTableOperations ops,
                CancellationToken ct
            ) =>
                body.EntryId <= 0
                    ? Results.BadRequest(new { error = "invalid_request" })
                    : Results.Ok(
                        await ops.DeleteLootEntryAsync(body, ctx.ActorEmail(), ct)
                            .ConfigureAwait(false)
                    ),
            Capabilities.Dashboard.OpsLootTablesManage,
            TagLootTables
        );
        MapPost(
            app,
            ApiOperations + "/loot-tables/bindings",
            async (
                HttpContext ctx,
                CreateLootBindingRequest body,
                LootTableOperations ops,
                CancellationToken ct
            ) =>
                body.FurnitureDefinitionId <= 0
                || string.IsNullOrWhiteSpace(body.PoolCode)
                || body.HitsRequired <= 0
                    ? Results.BadRequest(new { error = "invalid_request" })
                    : Results.Ok(
                        await ops.CreateLootBindingAsync(body, ctx.ActorEmail(), ct)
                            .ConfigureAwait(false)
                    ),
            Capabilities.Dashboard.OpsLootTablesManage,
            TagLootTables
        );
        MapPost(
            app,
            ApiOperations + "/loot-tables/bindings/update",
            async (
                HttpContext ctx,
                UpdateLootBindingRequest body,
                LootTableOperations ops,
                CancellationToken ct
            ) =>
                body.BindingId <= 0
                || body.FurnitureDefinitionId <= 0
                || string.IsNullOrWhiteSpace(body.PoolCode)
                || body.HitsRequired <= 0
                    ? Results.BadRequest(new { error = "invalid_request" })
                    : Results.Ok(
                        await ops.UpdateLootBindingAsync(body, ctx.ActorEmail(), ct)
                            .ConfigureAwait(false)
                    ),
            Capabilities.Dashboard.OpsLootTablesManage,
            TagLootTables
        );
        MapPost(
            app,
            ApiOperations + "/loot-tables/bindings/delete",
            async (
                HttpContext ctx,
                DeleteLootBindingRequest body,
                LootTableOperations ops,
                CancellationToken ct
            ) =>
                body.BindingId <= 0
                    ? Results.BadRequest(new { error = "invalid_request" })
                    : Results.Ok(
                        await ops.DeleteLootBindingAsync(body, ctx.ActorEmail(), ct)
                            .ConfigureAwait(false)
                    ),
            Capabilities.Dashboard.OpsLootTablesManage,
            TagLootTables
        );
        MapPost(
            app,
            ApiOperations + "/loot-tables/reload",
            async (
                HttpContext ctx,
                ReloadLootTablesRequest body,
                LootTableOperations ops,
                CancellationToken ct
            ) =>
                Results.Ok(
                    await ops.ReloadLootTablesAsync(body, ctx.ActorEmail(), ct)
                        .ConfigureAwait(false)
                ),
            Capabilities.Dashboard.OpsLootTablesManage,
            TagLootTables
        );
    }
}
