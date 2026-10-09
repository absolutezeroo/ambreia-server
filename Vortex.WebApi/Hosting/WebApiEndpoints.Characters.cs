using System;
using System.Text.Json;
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
using Vortex.Primitives.Rpg.Items;
using Vortex.WebApi.Http.Requests;
using Vortex.WebApi.Http.Responses;

namespace Vortex.WebApi.Hosting;

/// <summary>
/// The character routes (spec Personnage §8): the classes on offer, and an XP grant for testing. A
/// character is created, given its class and entered on the game socket (spec F3a §4), not here.
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
                "/api/admin/characters/{playerId:int}/xp",
                async Task<IResult> (int playerId, GrantXpRequest body, IGrainFactory grains) =>
                {
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
            .RequireCapability(Capabilities.Rpg.GrantXp)
            .WithTags(TagCharacters)
            .WithName("GrantCharacterXp")
            .WithSummary("Grant XP to a character, as a server system would (test command).")
            .Produces<GrantXpResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces<ApiErrorResponse>(StatusCodes.Status404NotFound);

        app.MapPost(
                "/api/admin/characters/{playerId:int}/items",
                async Task<IResult> (int playerId, GrantItemsRequest body, IGrainFactory grains) =>
                {
                    if (body is null || !body.IsValid)
                    {
                        return TypedResults.BadRequest(new ApiErrorResponse("invalid_request"));
                    }

                    ItemGrantResult result = await grains
                        .GetPlayerCharacterGrain(new PlayerId(playerId))
                        .GrantItemsAsync(
                            body.GrantKey!,
                            [
                                new ItemGrantLine
                                {
                                    DefinitionId = body.DefinitionId!,
                                    Quantity = body.Quantity,
                                },
                            ]
                        )
                        .ConfigureAwait(false);
                    string code = JsonNamingPolicy.SnakeCaseLower.ConvertName(
                        result.Outcome.ToString()
                    );

                    return result.Outcome switch
                    {
                        ItemOutcome.Ok or ItemOutcome.DuplicateGrant => TypedResults.Ok(
                            new GrantItemsResponse(code, result.ToBag, result.ToParcels)
                        ),
                        ItemOutcome.NoCharacter => TypedResults.NotFound(
                            new ApiErrorResponse("character_not_found")
                        ),
                        ItemOutcome.UnknownItem or ItemOutcome.InvalidQuantity =>
                            TypedResults.BadRequest(new ApiErrorResponse(code)),
                        ItemOutcome.ParcelsFull => TypedResults.Conflict(
                            new ApiErrorResponse(code)
                        ),
                        _ => throw new InvalidOperationException(
                            $"No answer for the grant outcome {result.Outcome}."
                        ),
                    };
                }
            )
            .RequireCapability(Capabilities.Economy.GrantItem)
            .WithTags(TagCharacters)
            .WithName("GrantCharacterItems")
            .WithSummary("Grant an item to a character, once per key (administration and tests).")
            .Produces<GrantItemsResponse>()
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces<ApiErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict);
    }
}
