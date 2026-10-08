namespace Vortex.WebApi.Services;

/// <summary>One avatar of an account, as <see cref="IWebApiPlayerService" /> lists them.</summary>
public sealed record AvatarInfo(
    string UniqueId,
    string Name,
    string Motto,
    string FigureString,
    string Gender,
    // The character, when the avatar has one (spec Personnage §8); null otherwise.
    string? ClassId = null,
    int? Level = null
);
