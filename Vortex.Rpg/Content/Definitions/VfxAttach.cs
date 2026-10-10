namespace Vortex.Rpg.Content.Definitions;

/// <summary>Where the client pins a sprite effect on an avatar.</summary>
public enum VfxAttach
{
    /// <summary>The centre of the drawn body (an impact, a projectile's ends).</summary>
    Body,

    /// <summary>The point of its location on the floor, drawn over it (an aura standing on the ground).</summary>
    Feet,

    /// <summary>The same point, drawn under it like its shadow (a circle, a sigil).</summary>
    Ground,
}
