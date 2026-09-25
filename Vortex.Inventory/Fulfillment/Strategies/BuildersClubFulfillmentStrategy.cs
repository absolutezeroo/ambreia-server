using System.Collections.Generic;
using Vortex.Primitives.Furniture.Enums;

namespace Vortex.Inventory.Fulfillment.Strategies;

/// <summary>
/// Builders Club membership days and furni-limit add-ons. <c>ExtraParam</c> is the amount, and it
/// multiplies like furniture does: purchase quantity times the product's own count.
/// </summary>
/// <remarks>
/// A param that will not parse promises nothing, as a chat style's does: a mistyped catalogue row
/// should not make the buyer pay for the rest of the bundle being refused.
/// </remarks>
internal sealed class BuildersClubFulfillmentStrategy : IFulfillmentStrategy
{
    public IReadOnlyList<ProductType> Handles { get; } =
    [ProductType.BuildersClubDays, ProductType.BuildersClubFurni];

    public void Contribute(in FulfillmentRequest request, FulfillmentPlanBuilder into)
    {
        if (!int.TryParse(request.Product.ExtraParam, out int amount) || amount <= 0)
        {
            return;
        }

        int total = amount * request.Quantity * System.Math.Max(1, request.Product.Quantity);

        if (request.Product.ProductType == ProductType.BuildersClubDays)
        {
            into.AddBuildersClubDays(total);
        }
        else
        {
            into.AddBuildersClubFurni(total);
        }
    }
}
