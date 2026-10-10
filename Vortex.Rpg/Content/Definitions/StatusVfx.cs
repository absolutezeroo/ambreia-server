namespace Vortex.Rpg.Content.Definitions;

/// <summary>What the client draws for a status: its aura, looped on the combatant while it lasts.</summary>
public sealed record StatusVfx
{
    public VfxPhase? Aura { get; init; }
}
