namespace Vortex.Primitives.Rpg.Content;

/// <summary>
/// A monster as the map editor and the zone validation see it (spec M1c §5.1): what a spawn names.
/// Its name is the text key <c>&lt;id&gt;.name</c>; rank and behaviour are written as in the content
/// files (<c>elite</c>, <c>ranged</c>).
/// </summary>
/// <param name="Retired">Kept for whoever holds it, no longer to be placed.</param>
public sealed record MonsterSummary(
    string Id,
    string Rank,
    string Behavior,
    bool Aggressive,
    string Figure,
    bool Retired
);
