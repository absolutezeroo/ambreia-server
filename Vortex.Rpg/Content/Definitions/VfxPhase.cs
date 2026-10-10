namespace Vortex.Rpg.Content.Definitions;

/// <summary>
/// One sprite effect the client plays at a moment of a skill: a sheet of its art
/// (<c>spells/&lt;folder&gt;/&lt;name&gt;</c> in the client, named <c>&lt;folder&gt;/&lt;name&gt;</c>),
/// pinned on the avatar's body or at its feet.
/// </summary>
public sealed record VfxPhase
{
    public required string Sheet { get; init; }

    public VfxAttach Attach { get; init; } = VfxAttach.Body;

    /// <summary>Played this long after the moment it belongs to (a hit after the area's flames rose).</summary>
    public int DelayMs { get; init; }
}
