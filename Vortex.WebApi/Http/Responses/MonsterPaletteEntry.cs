namespace Vortex.WebApi.Http.Responses;

/// <summary>A monster the map editor can place in a spawn (spec M1c §5.1); its name is the text key
/// <c>&lt;id&gt;.name</c> of <c>/api/public/content/texts/{language}</c>.</summary>
public sealed record MonsterPaletteEntry(
    string Id,
    string Rank,
    string Behavior,
    bool Aggressive,
    string Figure
);
