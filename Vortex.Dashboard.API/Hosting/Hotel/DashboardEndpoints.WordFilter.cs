using System;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Vortex.Dashboard.API.Api.Hotel;
using Vortex.Dashboard.API.Api.Hotel.Contracts;
using Vortex.Dashboard.API.Operations.Hotel;
using Vortex.Dashboard.API.Operations.Hotel.Contracts;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Permissions;

namespace Vortex.Dashboard.API.Hosting;

/// <summary>
/// The hotel-wide word filter: its list, its journal, a dry run, and the three edits — all behind
/// one capability, <see cref="Capabilities.Dashboard.OpsWordFilterManage"/>.
/// </summary>
internal static partial class DashboardEndpoints
{
    private const string TagWordFilter = "Word filter";
    private const string ApiWordFilter = ApiV1 + "/wordfilter";
    private const string OpsWordFilter = ApiOperations + "/wordfilter";

    public static void MapWordFilterReads(WebApplication app)
    {
        MapReadGet<WordFilterWordList>(
            app,
            ApiWordFilter + "/words",
            (WordFilterReads reads, CancellationToken ct) => OkAsync(reads.WordsAsync(ct)),
            Capabilities.Dashboard.OpsWordFilterManage,
            TagWordFilter
        );
        MapReadGet<WordFilterHitPage>(
            app,
            ApiWordFilter + "/hits",
            (HttpContext ctx, WordFilterReads reads, CancellationToken ct) =>
                OkAsync(reads.HitsAsync(ctx.QueryAsNameValues(), ct)),
            Capabilities.Dashboard.OpsWordFilterManage,
            TagWordFilter
        );
        // A GET, not an operation: it changes nothing, so it takes no reason and leaves no audit.
        MapReadGet<WordFilterTestResult>(
            app,
            ApiWordFilter + "/test",
            (string? text, WordFilterReads reads) => Results.Ok(reads.Test(text)),
            Capabilities.Dashboard.OpsWordFilterManage,
            TagWordFilter
        );
    }

    public static void MapWordFilterOperations(WebApplication app)
    {
        MapPost(
            app,
            OpsWordFilter + "/words",
            async (
                HttpContext ctx,
                AddWordFilterWordRequest body,
                WordFilterOperations ops,
                CancellationToken ct
            ) =>
                !IsWordShaped(body.Word, body.Action, body.Match)
                    ? Results.BadRequest(new { error = "invalid_request" })
                    : Results.Ok(
                        await ops.AddAsync(body, ctx.ActorEmail(), ct).ConfigureAwait(false)
                    ),
            Capabilities.Dashboard.OpsWordFilterManage,
            TagWordFilter
        );
        MapPost(
            app,
            OpsWordFilter + "/words/update",
            async (
                HttpContext ctx,
                UpdateWordFilterWordRequest body,
                WordFilterOperations ops,
                CancellationToken ct
            ) =>
                body.Id <= 0 || !IsWordShaped(body.Word, body.Action, body.Match)
                    ? Results.BadRequest(new { error = "invalid_request" })
                    : Results.Ok(
                        await ops.UpdateAsync(body, ctx.ActorEmail(), ct).ConfigureAwait(false)
                    ),
            Capabilities.Dashboard.OpsWordFilterManage,
            TagWordFilter
        );
        MapPost(
            app,
            OpsWordFilter + "/words/remove",
            async (
                HttpContext ctx,
                RemoveWordFilterWordRequest body,
                WordFilterOperations ops,
                CancellationToken ct
            ) =>
                body.Id <= 0
                    ? Results.BadRequest(new { error = "invalid_request" })
                    : Results.Ok(
                        await ops.RemoveAsync(body, ctx.ActorEmail(), ct).ConfigureAwait(false)
                    ),
            Capabilities.Dashboard.OpsWordFilterManage,
            TagWordFilter
        );
    }

    /// <summary>
    /// The HTTP half of the validation: a word, and enums that name something (JSON numbers bind to
    /// any int). Length and the "has a letter" rule are the admin service's, which answers them.
    /// </summary>
    private static bool IsWordShaped(
        string? word,
        WordFilterAction action,
        WordFilterMatch match
    ) => !string.IsNullOrWhiteSpace(word) && Enum.IsDefined(action) && Enum.IsDefined(match);
}
