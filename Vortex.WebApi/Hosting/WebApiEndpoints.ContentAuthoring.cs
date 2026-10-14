using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Rpg.Content;
using Vortex.Primitives.Rpg.Content.Authoring;
using Vortex.WebApi.Http.Requests;
using Vortex.WebApi.Http.Responses;
using Vortex.WebApi.Session;

namespace Vortex.WebApi.Hosting;

/// <summary>
/// The content one document at a time (content authoring §4, §5), for the Game Editor: every route
/// behind content.publish; a write is JSON only (no cross-site form can send it), 256 KB at most,
/// written at the version it was read at, through the whole-pack import.
/// </summary>
internal static partial class WebApiEndpoints
{
    /// <summary>One document and its texts: far less than a pack.</summary>
    private const int MaxContentDocumentBytes = 256 * 1024;

    private static void MapContentAuthoring(WebApplication app)
    {
        RouteGroupBuilder content = app.MapGroup("/api/content").WithTags(TagContent);

        content
            .MapGet(
                "/documents",
                async Task<IResult> (
                    string kind,
                    IContentAuthoring authoring,
                    CancellationToken ct
                ) =>
                    await authoring.ListAsync(kind, ct).ConfigureAwait(false) is { } list
                        ? TypedResults.Ok(list)
                        : TypedResults.NotFound(
                            new ApiErrorResponse(ContentWriteResult.UnknownKind)
                        )
            )
            .RequireCapability(Capabilities.Content.Publish)
            .WithName("ContentDocuments")
            .Produces<ContentDocumentList>()
            .Produces<ApiErrorResponse>(StatusCodes.Status404NotFound);

        content
            .MapGet(
                "/documents/{id}",
                async Task<IResult> (
                    string id,
                    IContentAuthoring authoring,
                    CancellationToken ct
                ) =>
                    await authoring.GetAsync(id, ct).ConfigureAwait(false) is { } document
                        ? TypedResults.Ok(document)
                        : TypedResults.NotFound(
                            new ApiErrorResponse(ContentWriteResult.UnknownDocument)
                        )
            )
            .RequireCapability(Capabilities.Content.Publish)
            .WithName("ContentDocument")
            .Produces<ContentDocument>()
            .Produces<ApiErrorResponse>(StatusCodes.Status404NotFound);

        content
            .MapPut(
                "/documents/{id}",
                async Task<IResult> (
                    string id,
                    HttpContext ctx,
                    WebApiSessionStore sessions,
                    IContentAuthoring authoring,
                    CancellationToken ct
                ) =>
                {
                    if (!ctx.Request.HasJsonContentType())
                    {
                        return TypedResults.StatusCode(StatusCodes.Status415UnsupportedMediaType);
                    }

                    string? json = await ReadDocumentAsync(ctx, ct, MaxContentDocumentBytes)
                        .ConfigureAwait(false);

                    if (json is null)
                    {
                        return TooLarge();
                    }

                    ContentPutRequest? request;

                    try
                    {
                        request = JsonSerializer.Deserialize<ContentPutRequest>(json, PackJson);
                    }
                    catch (JsonException)
                    {
                        request = null;
                    }

                    if (request is not { IsValid: true })
                    {
                        return TypedResults.BadRequest(
                            new ApiErrorResponse("content.invalid_request")
                        );
                    }

                    ContentWrite write = new(
                        id,
                        request.Kind!,
                        request.BaseVersion,
                        request.Path,
                        request.Source!,
                        request.Texts?.ToDictionary(
                            t => t.Key,
                            t => (IReadOnlyDictionary<string, string>)t.Value
                        )
                    );

                    return WriteOutcome(
                        await authoring
                            .PutAsync(write, ctx.AccountId(sessions)!.Value, ct)
                            .ConfigureAwait(false)
                    );
                }
            )
            .RequireCapability(Capabilities.Content.Publish)
            .WithName("ContentDocumentPut")
            .Accepts<ContentPutRequest>("application/json")
            .Produces<ContentImportResult>()
            .Produces<ContentImportResult>(StatusCodes.Status400BadRequest)
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status413PayloadTooLarge)
            .Produces(StatusCodes.Status415UnsupportedMediaType);

        content
            .MapDelete(
                "/documents/{id}",
                async Task<IResult> (
                    string id,
                    long baseVersion,
                    string? replaceWith,
                    HttpContext ctx,
                    WebApiSessionStore sessions,
                    IContentAuthoring authoring,
                    CancellationToken ct
                ) =>
                    WriteOutcome(
                        await authoring
                            .DeleteAsync(
                                id,
                                baseVersion,
                                replaceWith,
                                ctx.AccountId(sessions)!.Value,
                                ct
                            )
                            .ConfigureAwait(false)
                    )
            )
            .RequireCapability(Capabilities.Content.Publish)
            .WithName("ContentDocumentDelete")
            .Produces<ContentImportResult>()
            .Produces<ContentImportResult>(StatusCodes.Status400BadRequest)
            .Produces<ApiErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict);

        content
            .MapGet(
                "/export",
                async Task<IResult> (IContentAuthoring authoring, CancellationToken ct) =>
                    TypedResults.Ok(await authoring.ExportAsync(ct).ConfigureAwait(false))
            )
            .RequireCapability(Capabilities.Content.Publish)
            .WithName("ContentExport")
            .Produces<ContentExport>();
    }

    /// <summary>200 with the import's answer; 400 with the validator's findings or the edit's code;
    /// 404 for no such document; 409 when the content moved past the version read.</summary>
    private static IResult WriteOutcome(ContentWriteResult written) =>
        written switch
        {
            { Error: ContentWriteResult.Stale } => TypedResults.Conflict(
                new ApiErrorResponse(ContentWriteResult.Stale)
            ),
            { Error: ContentWriteResult.UnknownDocument } => TypedResults.NotFound(
                new ApiErrorResponse(ContentWriteResult.UnknownDocument)
            ),
            { Error: { } error } => TypedResults.BadRequest(new ApiErrorResponse(error)),
            { Result: { Imported: true } imported } => TypedResults.Ok(imported),
            { Result: { } refused } => TypedResults.BadRequest(refused),
            _ => TypedResults.Problem(),
        };
}
