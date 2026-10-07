using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Editing;
using Vortex.WebApi.Http;
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
                async Task<IResult> (
                    HttpContext ctx,
                    WebApiSessionStore sessions,
                    IPermissionService permissions,
                    IZoneEditorService zones,
                    CancellationToken ct
                ) =>
                    await DenyAsync(ctx, sessions, permissions, Capabilities.World.ZoneEdit, ct)
                        .ConfigureAwait(false)
                    ?? TypedResults.Ok(await zones.ListAsync(ct).ConfigureAwait(false))
            )
            .WithName("EditorListZones")
            .Produces<IReadOnlyList<ZoneSummary>>();

        editor
            .MapPost(
                "/zones",
                async Task<IResult> (
                    HttpContext ctx,
                    CreateZoneRequest body,
                    WebApiSessionStore sessions,
                    IPermissionService permissions,
                    IZoneEditorService zones,
                    CancellationToken ct
                ) =>
                {
                    IResult? denied = await DenyAsync(
                            ctx,
                            sessions,
                            permissions,
                            Capabilities.World.ZoneEdit,
                            ct
                        )
                        .ConfigureAwait(false);

                    if (denied is not null)
                    {
                        return denied;
                    }

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
            .WithName("EditorCreateZone")
            .Produces<ZoneSummary>();

        editor
            .MapGet(
                "/zones/{key}/versions",
                async Task<IResult> (
                    HttpContext ctx,
                    string key,
                    WebApiSessionStore sessions,
                    IPermissionService permissions,
                    IZoneEditorService zones,
                    CancellationToken ct
                ) =>
                    await DenyAsync(ctx, sessions, permissions, Capabilities.World.ZoneEdit, ct)
                        .ConfigureAwait(false)
                    ?? TypedResults.Ok(await zones.GetVersionsAsync(key, ct).ConfigureAwait(false))
            )
            .WithName("EditorZoneVersions")
            .Produces<IReadOnlyList<ZoneVersionInfo>>();

        editor
            .MapGet(
                "/zones/{key}/document",
                async Task<IResult> (
                    HttpContext ctx,
                    string key,
                    int? version,
                    WebApiSessionStore sessions,
                    IPermissionService permissions,
                    IZoneEditorService zones,
                    CancellationToken ct
                ) =>
                {
                    IResult? denied = await DenyAsync(
                            ctx,
                            sessions,
                            permissions,
                            Capabilities.World.ZoneEdit,
                            ct
                        )
                        .ConfigureAwait(false);

                    if (denied is not null)
                    {
                        return denied;
                    }

                    string? json = await zones
                        .GetDocumentAsync(key, version, ct)
                        .ConfigureAwait(false);

                    return json is null
                        ? TypedResults.NotFound()
                        : TypedResults.Text(json, "application/json", Encoding.UTF8);
                }
            )
            .WithName("EditorZoneDocument");

        editor
            .MapPost(
                "/zones/{key}/validate",
                async Task<IResult> (
                    HttpContext ctx,
                    string key,
                    WebApiSessionStore sessions,
                    IPermissionService permissions,
                    IZoneEditorService zones,
                    CancellationToken ct
                ) =>
                {
                    IResult? denied = await DenyAsync(
                            ctx,
                            sessions,
                            permissions,
                            Capabilities.World.ZoneEdit,
                            ct
                        )
                        .ConfigureAwait(false);

                    if (denied is not null)
                    {
                        return denied;
                    }

                    string? json = await ReadDocumentAsync(ctx, ct).ConfigureAwait(false);

                    return json is null
                        ? TooLarge()
                        : TypedResults.Ok(
                            await zones.ValidateAsync(key, json, ct).ConfigureAwait(false)
                        );
                }
            )
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
                    IPermissionService permissions,
                    IZoneEditorService zones,
                    CancellationToken ct
                ) =>
                {
                    IResult? denied = await DenyAsync(
                            ctx,
                            sessions,
                            permissions,
                            Capabilities.World.ZoneEdit,
                            ct
                        )
                        .ConfigureAwait(false);

                    if (denied is not null)
                    {
                        return denied;
                    }

                    string? json = await ReadDocumentAsync(ctx, ct).ConfigureAwait(false);

                    if (json is null)
                    {
                        return TooLarge();
                    }

                    ZoneSaveResult result = await zones
                        .SaveAsync(
                            key,
                            json,
                            ctx.AccountId(sessions)!.Value,
                            comment ?? string.Empty,
                            ct
                        )
                        .ConfigureAwait(false);

                    return result.Saved ? TypedResults.Ok(result) : TypedResults.BadRequest(result);
                }
            )
            .WithName("EditorSaveZone")
            .Produces<ZoneSaveResult>()
            .Produces<ZoneSaveResult>(StatusCodes.Status400BadRequest);

        editor
            .MapPost(
                "/zones/{key}/publish/{version:int}",
                async Task<IResult> (
                    HttpContext ctx,
                    string key,
                    int version,
                    WebApiSessionStore sessions,
                    IPermissionService permissions,
                    IZoneEditorService zones,
                    CancellationToken ct
                ) =>
                {
                    IResult? denied = await DenyAsync(
                            ctx,
                            sessions,
                            permissions,
                            Capabilities.World.ZonePublish,
                            ct
                        )
                        .ConfigureAwait(false);

                    if (denied is not null)
                    {
                        return denied;
                    }

                    ZoneSaveResult result = await zones
                        .PublishAsync(key, version, ct)
                        .ConfigureAwait(false);

                    return result.Published
                        ? TypedResults.Ok(result)
                        : TypedResults.BadRequest(result);
                }
            )
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
                    IPermissionService permissions,
                    IZoneEditorService zones,
                    CancellationToken ct
                ) =>
                {
                    // Taking someone else's lock is a publisher's call.
                    string capability =
                        force == true
                            ? Capabilities.World.ZonePublish
                            : Capabilities.World.ZoneEdit;
                    IResult? denied = await DenyAsync(ctx, sessions, permissions, capability, ct)
                        .ConfigureAwait(false);

                    if (denied is not null)
                    {
                        return denied;
                    }

                    bool locked = await zones
                        .TryLockAsync(key, ctx.AccountId(sessions)!.Value, force == true, ct)
                        .ConfigureAwait(false);

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
                    IPermissionService permissions,
                    IZoneEditorService zones,
                    CancellationToken ct
                ) =>
                {
                    IResult? denied = await DenyAsync(
                            ctx,
                            sessions,
                            permissions,
                            Capabilities.World.ZoneEdit,
                            ct
                        )
                        .ConfigureAwait(false);

                    if (denied is not null)
                    {
                        return denied;
                    }

                    await zones
                        .UnlockAsync(key, ctx.AccountId(sessions)!.Value, ct)
                        .ConfigureAwait(false);

                    return TypedResults.Ok(EmptyResponse.Instance);
                }
            )
            .WithName("EditorUnlockZone");

        editor
            .MapGet(
                "/furniture",
                async Task<IResult> (
                    HttpContext ctx,
                    WebApiSessionStore sessions,
                    IPermissionService permissions,
                    IZoneEditorService zones,
                    CancellationToken ct
                ) =>
                    await DenyAsync(ctx, sessions, permissions, Capabilities.World.ZoneEdit, ct)
                        .ConfigureAwait(false)
                    ?? TypedResults.Ok(zones.GetPalette())
            )
            .WithName("EditorFurniturePalette")
            .Produces<IReadOnlyList<FurniturePaletteEntry>>();
    }

    /// <summary>401 without a session, 403 without the capability, null when allowed.</summary>
    private static async Task<IResult?> DenyAsync(
        HttpContext ctx,
        WebApiSessionStore sessions,
        IPermissionService permissions,
        string capability,
        CancellationToken ct
    )
    {
        int? accountId = ctx.AccountId(sessions);

        if (accountId is null)
        {
            return Unauthorized();
        }

        PermissionSet granted = await permissions
            .ResolveForAccountAsync(accountId.Value, ct)
            .ConfigureAwait(false);

        return granted.Has(capability) ? null : new ForbiddenError("editor.forbidden");
    }

    /// <summary>The raw document, or null when it is larger than <see cref="MaxDocumentBytes"/>.</summary>
    private static async Task<string?> ReadDocumentAsync(HttpContext ctx, CancellationToken ct)
    {
        if (ctx.Request.ContentLength > MaxDocumentBytes)
        {
            return null;
        }

        using StreamReader reader = new(ctx.Request.Body, Encoding.UTF8);
        char[] buffer = new char[MaxDocumentBytes + 1];
        int read = await reader.ReadBlockAsync(buffer.AsMemory(), ct).ConfigureAwait(false);

        return read > MaxDocumentBytes ? null : new string(buffer, 0, read);
    }

    private static IResult TooLarge() =>
        TypedResults.Problem(statusCode: StatusCodes.Status413PayloadTooLarge);
}
