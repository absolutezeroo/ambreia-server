using System.Collections.Generic;
using Vortex.Primitives.Furniture.Enums;

namespace Vortex.Inventory.Fulfillment.Strategies;

/// <summary>
/// A chat bubble. <c>ExtraParam</c> is the style id, as the client's own library numbers it.
/// </summary>
/// <remarks>
/// Not multiplied, for the reason a badge is not: a style is unlocked or it is not, so promising
/// five would promise four the grant refuses anyway. A param that will not parse promises nothing
/// rather than throwing — a mistyped catalogue row should not make the buyer pay for the rest of the
/// bundle being refused.
/// </remarks>
internal sealed class ChatStyleFulfillmentStrategy : IFulfillmentStrategy
{
    public IReadOnlyList<ProductType> Handles { get; } = [ProductType.ChatStyle];

    public void Contribute(in FulfillmentRequest request, FulfillmentPlanBuilder into)
    {
        if (!int.TryParse(request.Product.ExtraParam, out int chatStyleId) || chatStyleId <= 0)
        {
            return;
        }

        into.AddChatStyle(chatStyleId);
    }
}
