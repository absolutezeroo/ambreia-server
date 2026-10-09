using Orleans;

namespace Vortex.Primitives.Rpg.Characters;

/// <summary>
/// A persistent status a zone hands back with health and mana (spec M1b §7.4): kept in the
/// character's memory, never written to the database, and put back on entering the next zone if
/// time is left.
/// </summary>
[GenerateSerializer, Immutable]
public sealed record CarriedStatus
{
    [Id(0)]
    public required string StatusId { get; init; }

    [Id(1)]
    public required int RemainingMs { get; init; }

    [Id(2)]
    public required int Stacks { get; init; }

    /// <summary>Per pulse or absorbed, as worked out when it was applied.</summary>
    [Id(3)]
    public required double Amount { get; init; }
}
