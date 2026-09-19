using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Vortex.Dashboard.API.Api;
using Vortex.Dashboard.API.Infrastructure;
using Vortex.Dashboard.API.Operations;
using Vortex.Dashboard.API.Operations.Safety;
using Vortex.Dashboard.API.Operations.Safety.Contracts;
using Vortex.Dashboard.API.Security;
using Vortex.Primitives.Moderation;
using Vortex.Primitives.Permissions;

namespace Vortex.Dashboard.API.Hosting;

internal static partial class DashboardEndpoints
{
    public static void MapModerationOperations(WebApplication app)
    {
        MapPost(
            app,
            ApiOperations + "/players/kick",
            async (
                HttpContext ctx,
                KickPlayerRequest body,
                ModerationOperations ops,
                CancellationToken ct
            ) =>
            {
                if (body.PlayerId <= 0)
                {
                    return Results.BadRequest(new { error = "invalid_request" });
                }

                return Results.Ok(
                    await ops.KickPlayerAsync(body, ctx.ActorEmail(), ct).ConfigureAwait(false)
                );
            },
            Capabilities.Dashboard.OpsKickPlayer,
            TagOperations
        );
        MapPost(
            app,
            ApiOperations + "/players/ban",
            async (
                HttpContext ctx,
                BanPlayerRequest body,
                ModerationOperations ops,
                CancellationToken ct
            ) =>
            {
                if (body.PlayerId <= 0 || !HasValidDuration(body.Permanent, body.DurationSeconds))
                {
                    return Results.BadRequest(new { error = "invalid_request" });
                }

                return Results.Ok(
                    await ops.BanPlayerAsync(body, ctx.ActorEmail(), ct).ConfigureAwait(false)
                );
            },
            Capabilities.Dashboard.OpsBanAccount,
            TagOperations
        );
        MapPost(
            app,
            ApiOperations + "/players/unban",
            async (
                HttpContext ctx,
                UnbanPlayerRequest body,
                ModerationOperations ops,
                CancellationToken ct
            ) =>
            {
                if (body.PlayerId <= 0)
                {
                    return Results.BadRequest(new { error = "invalid_request" });
                }

                return Results.Ok(
                    await ops.UnbanPlayerAsync(body, ctx.ActorEmail(), ct).ConfigureAwait(false)
                );
            },
            Capabilities.Dashboard.OpsBanAccount,
            TagOperations
        );
        MapPost(
            app,
            ApiOperations + "/players/mute",
            async (
                HttpContext ctx,
                MutePlayerRequest body,
                ModerationOperations ops,
                CancellationToken ct
            ) =>
            {
                if (body.PlayerId <= 0 || body.DurationSeconds <= 0)
                {
                    return Results.BadRequest(new { error = "invalid_request" });
                }

                return Results.Ok(
                    await ops.MutePlayerAsync(body, ctx.ActorEmail(), ct).ConfigureAwait(false)
                );
            },
            Capabilities.Dashboard.OpsMutePlayer,
            TagOperations
        );
        MapPost(
            app,
            ApiOperations + "/players/trading-lock",
            async (
                HttpContext ctx,
                TradingLockRequest body,
                ModerationOperations ops,
                CancellationToken ct
            ) =>
            {
                if (body.PlayerId <= 0 || !HasValidDuration(body.Permanent, body.DurationSeconds))
                {
                    return Results.BadRequest(new { error = "invalid_request" });
                }

                return Results.Ok(
                    await ops.TradingLockAsync(body, ctx.ActorEmail(), ct).ConfigureAwait(false)
                );
            },
            Capabilities.Dashboard.OpsTradingLock,
            TagOperations
        );
        MapPost(
            app,
            ApiOperations + "/players/trading-unlock",
            async (
                HttpContext ctx,
                TradingUnlockRequest body,
                ModerationOperations ops,
                CancellationToken ct
            ) =>
            {
                if (body.PlayerId <= 0)
                {
                    return Results.BadRequest(new { error = "invalid_request" });
                }

                return Results.Ok(
                    await ops.TradingUnlockAsync(body, ctx.ActorEmail(), ct).ConfigureAwait(false)
                );
            },
            Capabilities.Dashboard.OpsTradingLock,
            TagOperations
        );
        // Erasure on request. Not a sanction, which is why it carries its own capability rather than
        // riding on the ones above: holding the power to ban somebody is no reason to hold the power
        // to erase the record of what they did.
        MapPost(
            app,
            ApiOperations + "/players/forensics-purge",
            async (
                HttpContext ctx,
                PurgePlayerForensicsRequest body,
                PrivacyOperations privacy,
                CancellationToken ct
            ) =>
            {
                // The reason is required by the same rule the sanctions follow, and more so here:
                // this is the one operation that destroys evidence, so "why" is the only thing
                // separating a lawful erasure from a cover-up.
                if (body.PlayerId <= 0 || string.IsNullOrWhiteSpace(body.Reason))
                {
                    return Results.BadRequest(new { error = "invalid_request" });
                }

                return Results.Ok(
                    await privacy
                        .PurgePlayerForensicsAsync(body, ctx.ActorEmail(), ct)
                        .ConfigureAwait(false)
                );
            },
            Capabilities.Dashboard.OpsForensicsPurge,
            TagOperations
        );
        MapReadGet<ImmutableArray<CfhIssueQueueEntrySnapshot>>(
            app,
            ApiOperations + "/cfh/queue",
            async (ModerationOperations ops, CancellationToken ct) =>
                Results.Ok(await ops.GetCfhQueueAsync(ct).ConfigureAwait(false)),
            Capabilities.Dashboard.OpsCfhManage,
            TagOperations
        );
        MapReadGet<ImmutableArray<CfhAppealSnapshot>>(
            app,
            ApiOperations + "/cfh/appeals",
            async (ModerationOperations ops, CancellationToken ct) =>
                Results.Ok(await ops.GetCfhAppealsAsync(ct).ConfigureAwait(false)),
            Capabilities.Dashboard.OpsCfhManage,
            TagOperations
        );
        MapPost(
            app,
            ApiOperations + "/cfh/appeals/resolve",
            async (
                HttpContext ctx,
                ResolveCfhAppealsRequest body,
                ModerationOperations ops,
                CancellationToken ct
            ) =>
            {
                if (body.IssueIds is null || body.IssueIds.Length == 0)
                {
                    return Results.BadRequest(new { error = "invalid_request" });
                }

                return Results.Ok(
                    await ops.ResolveCfhAppealsAsync(body, ctx.ActorEmail(), ct)
                        .ConfigureAwait(false)
                );
            },
            Capabilities.Dashboard.OpsCfhManage,
            TagOperations
        );
        MapPost(
            app,
            ApiOperations + "/cfh/pick",
            async (
                HttpContext ctx,
                PickCfhTicketsRequest body,
                ModerationOperations ops,
                CancellationToken ct
            ) =>
            {
                if (body.IssueIds is null || body.IssueIds.Length == 0)
                {
                    return Results.BadRequest(new { error = "invalid_request" });
                }

                return Results.Ok(
                    await ops.PickCfhTicketsAsync(body, ctx.ActorEmail(), ct).ConfigureAwait(false)
                );
            },
            Capabilities.Dashboard.OpsCfhManage,
            TagOperations
        );
        MapPost(
            app,
            ApiOperations + "/cfh/close",
            async (
                HttpContext ctx,
                CloseCfhTicketsRequest body,
                ModerationOperations ops,
                CancellationToken ct
            ) =>
            {
                if (body.IssueIds is null || body.IssueIds.Length == 0 || body.Reason is < 1 or > 3)
                {
                    return Results.BadRequest(new { error = "invalid_request" });
                }

                return Results.Ok(
                    await ops.CloseCfhTicketsAsync(body, ctx.ActorEmail(), ct).ConfigureAwait(false)
                );
            },
            Capabilities.Dashboard.OpsCfhManage,
            TagOperations
        );
        MapPost(
            app,
            ApiOperations + "/cfh/release",
            async (
                HttpContext ctx,
                ReleaseCfhTicketsRequest body,
                ModerationOperations ops,
                CancellationToken ct
            ) =>
            {
                if (body.IssueIds is null || body.IssueIds.Length == 0)
                {
                    return Results.BadRequest(new { error = "invalid_request" });
                }

                return Results.Ok(
                    await ops.ReleaseCfhTicketsAsync(body, ctx.ActorEmail(), ct)
                        .ConfigureAwait(false)
                );
            },
            Capabilities.Dashboard.OpsCfhManage,
            TagOperations
        );
    }
}
