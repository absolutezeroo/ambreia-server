using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Avatar;

namespace Vortex.WebApi.Http.Requests;

/// <remarks>
/// <see cref="Name"/> is optional: the client posts this route with an empty name right after
/// registering, and <c>WebApiPlayerService.CreateAvatarAsync</c> assigns a placeholder that the
/// client's onboarding step replaces. Everything else about the request is unchanged.
/// </remarks>
/// <param name="ClassId">Required for a named avatar once the content has classes (spec Personnage
/// §8); the registration avatar, created without a name, chooses its class later.</param>
public sealed record CreateAvatarRequest(
    string? Name,
    string? Figure,
    string? Gender,
    string? ClassId = null
)
{
    // Blank stays valid — that is the registration path, which wants the placeholder. A name that IS
    // supplied has to be one the player could have picked in-game; WebApiPlayerService refuses it
    // again at the sink, this is only so the caller gets a 400 rather than a 409 about a name clash
    // that is not what went wrong.
    //
    // A look past the column width used to reach the database and come back as a 500.
    public bool IsValid =>
        (string.IsNullOrWhiteSpace(Name) || NameShape.IsWellFormed(Name))
        && (Figure is null || Figure.Length <= FigureString.MaxLength);
}
