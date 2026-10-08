using System.Collections.Generic;
using Vortex.Primitives.Rpg.Items;

namespace Vortex.Rpg.Items;

/// <summary>What a grant did: its outcome, how much went to the bag and to parcels, and which
/// copies received what.</summary>
public sealed record GrantPlan(
    ItemOutcome Outcome,
    int ToBag,
    int ToParcels,
    IReadOnlyList<ItemQuantity> Placed
)
{
    public static GrantPlan Refused(ItemOutcome outcome) => new(outcome, 0, 0, []);
}
