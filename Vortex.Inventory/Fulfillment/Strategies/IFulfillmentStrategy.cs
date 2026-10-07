using System.Collections.Generic;
using Vortex.Primitives.Bots;
using Vortex.Primitives.Catalog.Snapshots;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Groups.Snapshots;
using Vortex.Primitives.Pets;

namespace Vortex.Inventory.Fulfillment.Strategies;

/// <summary>
/// What one kind of catalog product contributes to a purchase.
/// </summary>
/// <remarks>
/// <para>
/// One class per <see cref="ProductType"/>, replacing the switch the planner used to carry. The
/// switch was not wrong — it was six cases and each already delegated to its own method — but a new
/// product type had to be remembered in two places, and the guard clauses that decide whether a
/// product is usable at all (<c>Badge</c> needs an extra param, <c>Effect</c> needs a parseable one)
/// were on the dispatch rather than with the rule they belong to.
/// </para>
/// <para>
/// Pure, like the planner around them: a strategy reads the product and writes into the builder. No
/// database, no grains, no clock. That is what lets the whole plan be computed before the wallet is
/// touched, which is what makes an unknown definition an error nothing has to compensate for.
/// </para>
/// </remarks>
internal interface IFulfillmentStrategy
{
    /// <summary>
    /// The product types this handles. A list rather than a single value because floor and wall
    /// furniture are the same rule — the difference is which table the row lands in, and the plan
    /// does not care.
    /// </summary>
    IReadOnlyList<ProductType> Handles { get; }

    void Contribute(in FulfillmentRequest request, FulfillmentPlanBuilder into);
}

/// <summary>
/// One product of a purchase, and everything a strategy is allowed to know about it.
/// </summary>
/// <param name="Quantity">
/// The purchase multiplier, already floored at one. The copies to grant are this times the product's
/// own count — ignoring the second collapsed every bundle to one of each item.
/// </param>
/// <param name="PurchaseExtraParam">
/// What the buyer typed, not what the catalogue holds: a pet's name, race and colour, or the name
/// for a bot. Newline-separated, and the strategy that needs it knows its own shape.
/// </param>
/// <param name="GuildIdentity">
/// Resolved by the caller because resolving it is a grain call, and this stays pure.
/// </param>
internal readonly record struct FulfillmentRequest(
    CatalogProductSnapshot Product,
    int Quantity,
    string PurchaseExtraParam,
    GuildFurniIdentitySnapshot? GuildIdentity
);

/// <summary>Collects what the strategies promise, then seals it into a <see cref="FulfillmentPlan"/>.</summary>
internal sealed class FulfillmentPlanBuilder
{
    private readonly List<PlannedFurniture> _furniture = [];
    private readonly List<string> _badgeCodes = [];
    private readonly List<PetCreateRequest> _pets = [];
    private readonly List<BotCreateRequest> _bots = [];
    private readonly List<PlannedEffect> _effects = [];

    public void AddFurniture(PlannedFurniture furniture) => _furniture.Add(furniture);

    public void AddBadge(string code) => _badgeCodes.Add(code);

    public void AddPet(PetCreateRequest pet) => _pets.Add(pet);

    public void AddBot(BotCreateRequest bot) => _bots.Add(bot);

    public void AddEffect(PlannedEffect effect) => _effects.Add(effect);

    public FulfillmentPlan Build() =>
        new()
        {
            Furniture = _furniture,
            BadgeCodes = _badgeCodes,
            Pets = _pets,
            Bots = _bots,
            Effects = _effects,
        };
}
