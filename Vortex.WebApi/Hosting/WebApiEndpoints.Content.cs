using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Rpg.Content;
using Vortex.WebApi.Http.Errors;
using Vortex.WebApi.Http.Requests;
using Vortex.WebApi.Http.Responses;
using Vortex.WebApi.Session;

namespace Vortex.WebApi.Hosting;

/// <summary>
/// The RPG content routes (spec Contenu §6, §7): the whole pack is imported behind
/// content.publish; the imported texts are public, for the client to load beside its own.
/// </summary>
internal static partial class WebApiEndpoints
{
    private const string TagContent = "Content";

    /// <summary>Every document of the game at once: far more than a zone, still bounded.</summary>
    private const int MaxPackBytes = 16 * 1024 * 1024;

    private static readonly JsonSerializerOptions PackJson = new(JsonSerializerDefaults.Web);

    private static void MapContent(WebApplication app)
    {
        app.MapPost(
                "/api/content/import",
                async Task<IResult> (
                    HttpContext ctx,
                    WebApiSessionStore sessions,
                    IPermissionService permissions,
                    IRpgContentService content,
                    CancellationToken ct
                ) =>
                {
                    IResult? denied = await DenyAsync(
                            ctx,
                            sessions,
                            permissions,
                            Capabilities.Content.Publish,
                            ct
                        )
                        .ConfigureAwait(false);

                    if (denied is not null)
                    {
                        return denied;
                    }

                    string? json = await ReadDocumentAsync(ctx, ct, MaxPackBytes)
                        .ConfigureAwait(false);

                    if (json is null)
                    {
                        return TooLarge();
                    }

                    ContentPack? pack;

                    try
                    {
                        pack = JsonSerializer.Deserialize<ContentPack>(json, PackJson);
                    }
                    catch (JsonException)
                    {
                        pack = null;
                    }

                    if (pack is null)
                    {
                        return TypedResults.BadRequest(
                            new ApiErrorResponse("content.invalid_pack")
                        );
                    }

                    ContentImportResult result = await content
                        .ImportAsync(pack, ctx.AccountId(sessions)!.Value, ct)
                        .ConfigureAwait(false);

                    return result.Imported
                        ? TypedResults.Ok(result)
                        : TypedResults.BadRequest(result);
                }
            )
            .WithTags(TagContent)
            .WithName("ContentImport")
            .Accepts<ContentPack>("application/json")
            .Produces<ContentImportResult>()
            .Produces<ContentImportResult>(StatusCodes.Status400BadRequest);

        app.MapGet(
                "/api/public/content/texts/{language}",
                async Task<IResult> (
                    string language,
                    IRpgContentService content,
                    CancellationToken ct
                ) =>
                    await content.GetTextsAsync(language, ct).ConfigureAwait(false) is { } body
                        ? TypedResults.Text(body, "text/plain; charset=utf-8")
                        : TypedResults.NotFound()
            )
            .WithTags(TagContent)
            .WithName("ContentTexts")
            .Produces<string>(StatusCodes.Status200OK, "text/plain");
    }
}
