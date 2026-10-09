namespace Vortex.Rpg.Content.Definitions;

/// <summary>A status applied on top of itself (spec M1b §7.1). Values are never renumbered.</summary>
public enum StatusStacking
{
    Replace = 1,
    Refresh = 2,
    Stack = 3,
    Ignore = 4,
}
