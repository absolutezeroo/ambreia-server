using Orleans;

namespace Vortex.Primitives.Orleans.Snapshots.Players;

/// <summary>
/// Everything the user object says about respect. Kept off <c>PlayerSummarySnapshot</c> because the
/// counters need the live config, and the summary is fetched on every room entry, figure change and
/// pet scratch — this one is read once per login.
/// </summary>
[GenerateSerializer, Immutable]
public sealed record PlayerRespectSnapshot
{
    /// <summary>Respects received, all-time. Shown on the player's own profile.</summary>
    [Id(0)]
    public required int Received { get; init; }

    /// <summary>Respects this player can still give today.</summary>
    [Id(1)]
    public required int Left { get; init; }

    /// <summary>Pets this player can still scratch today.</summary>
    [Id(2)]
    public required int PetLeft { get; init; }

    /// <summary>Refills of <see cref="Left"/> still available today.</summary>
    [Id(3)]
    public required int ReplenishesLeft { get; init; }

    /// <summary>Today's full budget, which is what a refill restores <see cref="Left"/> to.</summary>
    [Id(4)]
    public required int MaxPerDay { get; init; }
}
