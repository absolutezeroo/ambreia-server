using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Orleans;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Primitives.Rpg.Characters.Grains;
using Vortex.Primitives.Rpg.Content;
using Vortex.WebApi.Http;
using Vortex.WebApi.Services;
using Vortex.WebApi.Session;

namespace Vortex.WebApi.Hosting;

/// <summary>
/// The character routes (spec Personnage §8): the classes on offer, and the class of an avatar that
/// has none — chosen through its grain, once, never written to the table from here.
/// </summary>
internal static partial class WebApiEndpoints
{
    private const string TagCharacters = "Characters";

    private static void MapCharacters(WebApplication app)
    {
        app.MapGet(
                "/api/public/content/classes",
                (IRpgContentService content) => TypedResults.Ok(content.GetClasses())
            )
            .WithTags(TagCharacters)
            .WithName("ContentClasses")
            .WithSummary("The classes a new character may take.");

        app.MapPost(
                "/api/user/avatars/{uniqueId}/class",
                async Task<IResult> (
                    string uniqueId,
                    HttpContext ctx,
                    ChooseClassRequest body,
                    WebApiSessionStore sessions,
                    IWebApiPlayerService players,
                    IGrainFactory grains,
                    CancellationToken ct
                ) =>
                {
                    int? accountId = ctx.AccountId(sessions);

                    if (accountId is null)
                    {
                        return Unauthorized();
                    }

                    if (body is null || !body.IsValid || !int.TryParse(uniqueId, out int playerId))
                    {
                        return TypedResults.BadRequest(new ApiErrorResponse("invalid_request"));
                    }

                    List<AvatarInfo> owned = await players
                        .GetAvatarsForAccountAsync(accountId.Value, ct)
                        .ConfigureAwait(false);

                    if (!owned.Exists(a => a.UniqueId == uniqueId))
                    {
                        return new ForbiddenError("avatar_not_owned");
                    }

                    ChooseClassOutcome outcome = await grains
                        .GetPlayerCharacterGrain(new PlayerId(playerId))
                        .ChooseClassAsync(body.ClassId!)
                        .ConfigureAwait(false);

                    return outcome switch
                    {
                        ChooseClassOutcome.Chosen => TypedResults.Ok(new EmptyResponse()),
                        ChooseClassOutcome.AlreadyChosen => TypedResults.Conflict(
                            new ApiErrorResponse("avatar.class_already_chosen")
                        ),
                        _ => TypedResults.BadRequest(new ApiErrorResponse("avatar.unknown_class")),
                    };
                }
            )
            .WithTags(TagCharacters)
            .WithName("ChooseAvatarClass")
            .WithSummary("Give an avatar without a class its class, once.")
            .Produces<EmptyResponse>()
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict);

        app.MapPost(
                "/api/admin/characters/{playerId:int}/xp",
                async Task<IResult> (
                    int playerId,
                    HttpContext ctx,
                    GrantXpRequest body,
                    WebApiSessionStore sessions,
                    IPermissionService permissions,
                    IGrainFactory grains,
                    CancellationToken ct
                ) =>
                {
                    IResult? denied = await DenyAsync(
                            ctx,
                            sessions,
                            permissions,
                            Capabilities.Rpg.GrantXp,
                            ct
                        )
                        .ConfigureAwait(false);

                    if (denied is not null)
                    {
                        return denied;
                    }

                    IPlayerCharacterGrain character = grains.GetPlayerCharacterGrain(
                        new PlayerId(playerId)
                    );
                    XpGrantOutcome outcome = await character
                        .GainXpAsync(body?.Amount ?? 0, "admin")
                        .ConfigureAwait(false);

                    if (outcome == XpGrantOutcome.NoCharacter)
                    {
                        return TypedResults.NotFound(new ApiErrorResponse("character_not_found"));
                    }

                    CharacterSummary? summary = await character
                        .GetSummaryAsync()
                        .ConfigureAwait(false);

                    return TypedResults.Ok(
                        new GrantXpResponse(
                            JsonNamingPolicy.SnakeCaseLower.ConvertName(outcome.ToString()),
                            summary?.Level ?? 0
                        )
                    );
                }
            )
            .WithTags(TagCharacters)
            .WithName("GrantCharacterXp")
            .WithSummary("Grant XP to a character, as a server system would (test command).")
            .Produces<GrantXpResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces<ApiErrorResponse>(StatusCodes.Status404NotFound);
    }

    /// <summary>The avatars with their character's class and level, read from their grains at once.</summary>
    private static async Task<List<AvatarInfo>> WithCharactersAsync(
        List<AvatarInfo> avatars,
        IGrainFactory grains
    )
    {
        CharacterSummary?[] summaries = await Task.WhenAll(
                avatars.Select(a =>
                    int.TryParse(a.UniqueId, out int id)
                        ? grains.GetPlayerCharacterGrain(new PlayerId(id)).GetSummaryAsync()
                        : Task.FromResult<CharacterSummary?>(null)
                )
            )
            .ConfigureAwait(false);

        return
        [
            .. avatars.Select(
                (a, i) => a with { ClassId = summaries[i]?.ClassId, Level = summaries[i]?.Level }
            ),
        ];
    }

    /// <summary>A named avatar needs a class once the content has classes; null when it may go on.</summary>
    private static string? ClassRefusal(CreateAvatarRequest body, IRpgContentService content)
    {
        if (string.IsNullOrWhiteSpace(body.Name))
        {
            // The registration avatar: named, and given its class, later (spec Personnage §2.4).
            return null;
        }

        IReadOnlyList<ClassSummary> classes = content.GetClasses();

        if (classes.Count == 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(body.ClassId))
        {
            return "avatar.class_required";
        }

        return classes.Any(c => c.Id == body.ClassId) ? null : "avatar.unknown_class";
    }
}
