using Orleans;

namespace Vortex.Primitives.Rpg.Items;

/// <summary>The answer to a grant: its outcome, and how much went to the bag and to parcels.</summary>
[GenerateSerializer, Immutable]
public sealed record ItemGrantResult
{
    [Id(0)]
    public required ItemOutcome Outcome { get; init; }

    [Id(1)]
    public int ToBag { get; init; }

    [Id(2)]
    public int ToParcels { get; init; }
}
