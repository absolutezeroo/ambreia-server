namespace Vortex.Rpg.Content.Definitions;

/// <summary>A sheet that flies from the caster's body to the target's; what follows waits for it to land.</summary>
public sealed record VfxProjectile
{
    public required string Sheet { get; init; }

    /// <summary>In the room's own pixels per second.</summary>
    public required int Speed { get; init; }
}
