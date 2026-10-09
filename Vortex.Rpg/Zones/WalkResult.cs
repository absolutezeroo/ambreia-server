namespace Vortex.Rpg.Zones;

/// <summary>What a walk request came to (spec M1a §4.2).</summary>
public enum WalkResult
{
    /// <summary>Walking, or already where it should be.</summary>
    Started = 1,

    /// <summary>No path leads there: the target is unreachable.</summary>
    NoPath = 2,

    /// <summary>The zone's path budget for this tick is spent: ask again next tick.</summary>
    BudgetSpent = 3,
}
