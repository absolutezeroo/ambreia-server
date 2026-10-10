using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Orleans;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rpg.Content;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Editing;
using Vortex.WebApi.Http.Errors;
using Vortex.WebApi.Http.Requests;
using Vortex.WebApi.Http.Responses;
using Vortex.WebApi.Services;
using Vortex.WebApi.Session;

namespace Vortex.WebApi.Hosting;

/// <summary>
/// The map editor's routes (spec 2026-10-07-rpg-world-map-editor §6.1). A session first, then a
/// capability: world.zone.edit to build, world.zone.publish to make a version live. Documents are
/// read raw so the service parses them itself and answers with findings instead of a bind error.
/// </summary>
internal static partial class WebApiEndpoints
{
    private const string TagEditor = "Editor";

    /// <summary>A 64 × 64 zone with heavy decor stays far below this.</summary>
    private const int MaxDocumentBytes = 4 * 1024 * 1024;

    private static void MapEditor(WebApplication app)
    {
        RouteGroupBuilder editor = app.MapGroup("/api/editor").WithTags(TagEditor);

        editor
            .MapGet(
                "/zones",
                async Task<IResult> (IZoneEditorService zones, CancellationToken ct) =>
                    TypedResults.Ok(await zones.ListAsync(ct).ConfigureAwait(false))
            )
            .RequireCapability(Capabilities.World.ZoneEdit)
            .WithName("EditorListZones")
            .Produces<IReadOnlyList<ZoneSummary>>();

        editor
            .MapPost(
                "/zones",
                async Task<IResult> (
                    HttpContext ctx,
                    CreateZoneRequest body,
                    WebApiSessionStore sessions,
                    IZoneEditorService zones,
                    CancellationToken ct
                ) =>
                {
                    if (body is null || !body.IsValid)
                    {
                        return TypedResults.BadRequest(new ApiErrorResponse("invalid_request"));
                    }

                    try
                    {
                        return TypedResults.Ok(
                            await zones
                                .CreateAsync(
                                    body.Key!,
                                    body.Name!,
                                    ctx.AccountId(sessions)!.Value,
                                    body.FromTemplate,
                                    ct
                                )
                                .ConfigureAwait(false)
                        );
                    }
                    catch (KeyNotFoundException)
                    {
                        return TypedResults.NotFound(
                            new ApiErrorResponse("editor.template_not_found")
                        );
                    }
                    catch (ArgumentException)
                    {
                        return TypedResults.BadRequest(
                            new ApiErrorResponse("editor.zone_key_invalid")
                        );
                    }
                    catch (InvalidOperationException)
                    {
                        return TypedResults.Conflict(new ApiErrorResponse("editor.zone_exists"));
                    }
                }
            )
            .RequireCapability(Capabilities.World.ZoneEdit)
            .WithName("EditorCreateZone")
            .Produces<ZoneSummary>();

        editor
            .MapGet(
                "/zones/{key}/versions",
                async Task<IResult> (string key, IZoneEditorService zones, CancellationToken ct) =>
                    TypedResults.Ok(await zones.GetVersionsAsync(key, ct).ConfigureAwait(false))
            )
            .RequireCapability(Capabilities.World.ZoneEdit)
            .WithName("EditorZoneVersions")
            .Produces<IReadOnlyList<ZoneVersionInfo>>();

        editor
            .MapGet(
                "/zones/{key}/document",
                async Task<IResult> (
                    string key,
                    string? version,
                    IZoneEditorService zones,
                    CancellationToken ct
                ) =>
                {
                    string? json;

                    if (version == "latest")
                    {
                        json = await zones.GetLatestDocumentAsync(key, ct).ConfigureAwait(false);
                    }
                    else if (version is null || int.TryParse(version, out _))
                    {
                        json = await zones
                            .GetDocumentAsync(key, version is null ? null : int.Parse(version), ct)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        return TypedResults.BadRequest(new ApiErrorResponse("invalid_request"));
                    }

                    return json is null
                        ? TypedResults.NotFound()
                        : TypedResults.Text(json, "application/json", Encoding.UTF8);
                }
            )
            .RequireCapability(Capabilities.World.ZoneEdit)
            .WithName("EditorZoneDocument");

        editor
            .MapPost(
                "/zones/{key}/validate",
                async Task<IResult> (
                    HttpContext ctx,
                    string key,
                    IZoneEditorService zones,
                    CancellationToken ct
                ) =>
                {
                    string? json = await ReadDocumentAsync(ctx, ct).ConfigureAwait(false);

                    return json is null
                        ? TooLarge()
                        : TypedResults.Ok(
                            await zones.ValidateAsync(key, json, ct).ConfigureAwait(false)
                        );
                }
            )
            .RequireCapability(Capabilities.World.ZoneEdit)
            .WithName("EditorValidateZone")
            .Produces<IReadOnlyList<ZoneFinding>>();

        editor
            .MapPost(
                "/zones/{key}/versions",
                async Task<IResult> (
                    HttpContext ctx,
                    string key,
                    string? comment,
                    WebApiSessionStore sessions,
                    IZoneEditorService zones,
                    CancellationToken ct
                ) =>
                {
                    string? json = await ReadDocumentAsync(ctx, ct).ConfigureAwait(false);

                    if (json is null)
                    {
                        return TooLarge();
                    }

                    try
                    {
                        ZoneSaveResult result = await zones
                            .SaveAsync(
                                key,
                                json,
                                ctx.AccountId(sessions)!.Value,
                                comment ?? string.Empty,
                                ct
                            )
                            .ConfigureAwait(false);

                        return SaveOutcome(result, result.Saved);
                    }
                    catch (KeyNotFoundException)
                    {
                        return TypedResults.NotFound();
                    }
                }
            )
            .RequireCapability(Capabilities.World.ZoneEdit)
            .WithName("EditorSaveZone")
            .Produces<ZoneSaveResult>()
            .Produces<ZoneSaveResult>(StatusCodes.Status400BadRequest);

        editor
            .MapPost(
                "/zones/{key}/publish/{version:int}",
                async Task<IResult> (
                    string key,
                    int version,
                    IZoneEditorService zones,
                    CancellationToken ct
                ) =>
                {
                    try
                    {
                        ZoneSaveResult result = await zones
                            .PublishAsync(key, version, ct)
                            .ConfigureAwait(false);

                        return SaveOutcome(result, result.Published);
                    }
                    catch (KeyNotFoundException)
                    {
                        return TypedResults.NotFound();
                    }
                }
            )
            .RequireCapability(Capabilities.World.ZonePublish)
            .WithName("EditorPublishZone")
            .Produces<ZoneSaveResult>()
            .Produces<ZoneSaveResult>(StatusCodes.Status400BadRequest);

        editor
            .MapPost(
                "/zones/{key}/lock",
                async Task<IResult> (
                    HttpContext ctx,
                    string key,
                    bool? force,
                    WebApiSessionStore sessions,
                    IZoneEditorService zones,
                    CancellationToken ct
                ) =>
                {
                    // Taking someone else's lock is a publisher's call: the one route whose
                    // capability depends on the request, so it asks the gate itself.
                    string capability =
                        force == true
                            ? Capabilities.World.ZonePublish
                            : Capabilities.World.ZoneEdit;
                    IResult? denied = await CapabilityGate
                        .DenyAsync(ctx, capability, ct)
                        .ConfigureAwait(false);

                    if (denied is not null)
                    {
                        return denied;
                    }

                    bool locked;

                    try
                    {
                        locked = await zones
                            .TryLockAsync(key, ctx.AccountId(sessions)!.Value, force == true, ct)
                            .ConfigureAwait(false);
                    }
                    catch (KeyNotFoundException)
                    {
                        return TypedResults.NotFound();
                    }

                    return locked
                        ? TypedResults.Ok(EmptyResponse.Instance)
                        : TypedResults.Conflict(new ApiErrorResponse("editor.zone_locked"));
                }
            )
            .WithName("EditorLockZone");

        editor
            .MapDelete(
                "/zones/{key}/lock",
                async Task<IResult> (
                    HttpContext ctx,
                    string key,
                    WebApiSessionStore sessions,
                    IZoneEditorService zones,
                    CancellationToken ct
                ) =>
                {
                    await zones
                        .UnlockAsync(key, ctx.AccountId(sessions)!.Value, ct)
                        .ConfigureAwait(false);

                    return TypedResults.Ok(EmptyResponse.Instance);
                }
            )
            .RequireCapability(Capabilities.World.ZoneEdit)
            .WithName("EditorUnlockZone");

        editor
            .MapPost(
                "/zones/{key}/test",
                async Task<IResult> (
                    HttpContext ctx,
                    string key,
                    int? x,
                    int? y,
                    WebApiSessionStore sessions,
                    IZoneEditorService zones,
                    IWebApiPlayerService players,
                    IWebApiAuthService auth,
                    [FromServices] IGrainFactory grains,
                    CancellationToken ct
                ) =>
                {
                    ZoneSummary? zone = (
                        await zones.ListAsync(ct).ConfigureAwait(false)
                    ).FirstOrDefault(z => z.Key == key);

                    if (zone is null)
                    {
                        return TypedResults.NotFound();
                    }

                    if (zone.PublishedVersion is null)
                    {
                        return TypedResults.Conflict(
                            new ApiErrorResponse("editor.zone_not_published")
                        );
                    }

                    // The account's first character is the one that plays.
                    int? playerId = await players
                        .FirstPlayerIdAsync(ctx.AccountId(sessions)!.Value, ct)
                        .ConfigureAwait(false);

                    if (playerId is null)
                    {
                        return TypedResults.NotFound(
                            new ApiErrorResponse("pocket.auth.no_avatars")
                        );
                    }

                    await grains
                        .GetZoneTestGrain()
                        .ExpectAsync(
                            new PlayerId(playerId.Value),
                            new RoomId(zone.RoomId),
                            x ?? -1,
                            y ?? -1
                        )
                        .ConfigureAwait(false);

                    (bool success, string? ticket, string? error) = await auth.GetSsoTokenAsync(
                            playerId.Value,
                            ctx.RemoteIp(),
                            ct
                        )
                        .ConfigureAwait(false);

                    return success
                        ? TypedResults.Ok(new ZoneTestTicket(ticket!, zone.RoomId))
                        : new ForbiddenError(error ?? "avatar_not_owned");
                }
            )
            .RequireCapability(Capabilities.World.ZoneEdit)
            .WithName("EditorTestZone")
            .WithSummary(
                "An SSO ticket whose sign-in starts in the published zone, on the cell given."
            )
            .Produces<ZoneTestTicket>();

        editor
            .MapGet("/furniture", (IZoneEditorService zones) => TypedResults.Ok(zones.GetPalette()))
            .RequireCapability(Capabilities.World.ZoneEdit)
            .WithName("EditorFurniturePalette")
            .Produces<IReadOnlyList<FurniturePaletteEntry>>();

        // What a spawn can name (spec M1c §5.1): the served monsters, the retired ones left out.
        editor
            .MapGet(
                "/monsters",
                (IRpgContentService content) =>
                    TypedResults.Ok(
                        content
                            .GetMonsters()
                            .Where(m => !m.Retired)
                            .Select(m => new MonsterPaletteEntry(
                                m.Id,
                                m.Rank,
                                m.Behavior,
                                m.Aggressive,
                                m.Figure
                            ))
                            .ToList()
                    )
            )
            .RequireCapability(Capabilities.World.ZoneEdit)
            .WithName("EditorMonsterPalette")
            .Produces<IReadOnlyList<MonsterPaletteEntry>>();
    }

    /// <summary>
    /// 200 when it went through; 409 when another save took the version number; 400 with the
    /// findings otherwise.
    /// </summary>
    private static IResult SaveOutcome(ZoneSaveResult result, bool succeeded) =>
        succeeded ? TypedResults.Ok(result)
        : result.Findings.Any(f => f.Code == "Conflict") ? TypedResults.Conflict(result)
        : TypedResults.BadRequest(result);

    /// <summary>The raw document, or null when it is larger than <paramref name="maxBytes"/>.</summary>
    private static async Task<string?> ReadDocumentAsync(
        HttpContext ctx,
        CancellationToken ct,
        int maxBytes = MaxDocumentBytes
    )
    {
        if (ctx.Request.ContentLength > maxBytes)
        {
            return null;
        }

        // The server stops reading past the limit, so the body is read once at its real size.
        if (ctx.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        {
            limit.MaxRequestBodySize = maxBytes;
        }

        try
        {
            using StreamReader reader = new(ctx.Request.Body, Encoding.UTF8);

            return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        }
        catch (BadHttpRequestException)
        {
            return null;
        }
    }

    private static IResult TooLarge() =>
        TypedResults.Problem(statusCode: StatusCodes.Status413PayloadTooLarge);
}
