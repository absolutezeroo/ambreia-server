using System.Collections.Frozen;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Vortex.Inventory.Fulfillment.Strategies;
using Vortex.Primitives.Catalog.Snapshots;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Groups.Snapshots;

namespace Vortex.Inventory.Fulfillment;

/// <summary>
/// Turns a catalog offer into the <see cref="FulfillmentPlan"/> it promises.
/// </summary>
/// <remarks>
/// <para>
/// Pure and deterministic on purpose. It reads the offer, the definition provider and the buyer's
/// guild identity, and returns data — no database, no grains, no clock. Which means it can run
/// before the wallet is touched, and every error it can raise is one the purchase never has to
/// compensate for.
/// </para>
/// <para>
/// The rules themselves live one per <see cref="ProductType"/> in
/// <see cref="Strategies"/>, and each has been a bug at least once: the copies granted are the
/// purchase quantity times the product's own count (ignoring the second collapsed every bundle to
/// one of each item); only string-array furni may carry a guild layout (stamping anything else
/// corrupts its stuff data); a bot without a figure is skipped rather than granted blank.
/// </para>
/// <para>
/// What this class keeps is the composition: which strategy answers for which product type, and the
/// order the plan is built in. Adding a product type is a class and a line in <see cref="Default"/>,
/// not a case in a switch that somebody has to find.
/// </para>
/// </remarks>
public sealed class CatalogFulfillmentPlanner
{
    private readonly FrozenDictionary<ProductType, IFulfillmentStrategy> _byType;

    public CatalogFulfillmentPlanner(
        IFurnitureDefinitionProvider definitions,
        ILogger<CatalogFulfillmentPlanner> logger
    )
        : this(Default(definitions, logger)) { }

    /// <summary>
    /// Takes the strategies explicitly. For tests that want one rule in isolation, and for whoever
    /// eventually needs a product type this assembly does not know about.
    /// </summary>
    internal CatalogFulfillmentPlanner(IEnumerable<IFulfillmentStrategy> strategies)
    {
        Dictionary<ProductType, IFulfillmentStrategy> byType = [];

        foreach (IFulfillmentStrategy strategy in strategies)
        {
            foreach (ProductType type in strategy.Handles)
            {
                // Last registration wins, so an explicit list can replace a built-in without having
                // to filter it out first.
                byType[type] = strategy;
            }
        }

        _byType = byType.ToFrozenDictionary();
    }

    /// <summary>The product types this emulator knows how to grant, and what grants them.</summary>
    internal static IReadOnlyList<IFulfillmentStrategy> Default(
        IFurnitureDefinitionProvider definitions,
        ILogger logger
    ) =>
        [
            new FurnitureFulfillmentStrategy(definitions),
            new BadgeFulfillmentStrategy(),
            new EffectFulfillmentStrategy(),
            new BotFulfillmentStrategy(logger),
            new PetFulfillmentStrategy(),
        ];

    /// <summary>
    /// Works out everything <paramref name="offer"/> owes the buyer.
    /// </summary>
    /// <param name="guildIdentity">
    /// The buyer's identity in the guild named by <paramref name="extraParam"/>, resolved by the
    /// caller because it is a grain call and this stays pure.
    /// </param>
    /// <exception cref="Vortex.Logging.VortexException">
    /// A product names a definition that does not exist.
    /// </exception>
    public FulfillmentPlan Plan(
        CatalogOfferSnapshot offer,
        string extraParam,
        int quantity,
        GuildFurniIdentitySnapshot? guildIdentity
    )
    {
        quantity = System.Math.Max(1, quantity);

        FulfillmentPlanBuilder builder = new();

        foreach (CatalogProductSnapshot product in offer.Products)
        {
            // A product type nothing answers for promises nothing. That is not an error: a catalogue
            // carries rows for features an emulator may not have, and refusing the purchase would
            // punish the buyer for the hotel's backlog. FulfillmentPlan.IsEmpty is how a caller tells
            // "granted nothing" apart from "granted something".
            if (!_byType.TryGetValue(product.ProductType, out IFulfillmentStrategy? strategy))
            {
                continue;
            }

            strategy.Contribute(
                new FulfillmentRequest(product, quantity, extraParam, guildIdentity),
                builder
            );
        }

        return builder.Build();
    }
}
