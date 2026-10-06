using System.Collections.Generic;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Pets;
using Vortex.Primitives.Rooms.Enums;

namespace Vortex.Inventory.Fulfillment.Strategies;

/// <summary>
/// A pet. The product's extra param carries the type; the purchase's carries the name, race and
/// colour the buyer chose in the dialog, newline-separated.
/// </summary>
/// <remarks>
/// Every field defaults rather than refusing: a pet named nothing is named "Pet", an unparseable
/// race is zero, a missing colour is white. The dialog is the only thing that fills these in, so a
/// shape this does not recognise is a client that sent something unexpected — and a plain pet is a
/// better answer to that than a purchase that fails after the buyer named it.
/// </remarks>
internal sealed class PetFulfillmentStrategy : IFulfillmentStrategy
{
    public IReadOnlyList<ProductType> Handles { get; } = [ProductType.Pet];

    public void Contribute(in FulfillmentRequest request, FulfillmentPlanBuilder into)
    {
        _ = int.TryParse(request.Product.ExtraParam, out int petType);

        string[] parts = request.PurchaseExtraParam.Split('\n');
        string petName = parts.Length > 0 ? parts[0].Trim() : "Pet";
        int race = parts.Length > 1 && int.TryParse(parts[1], out int r) ? r : 0;
        string color = parts.Length > 2 ? parts[2].Trim() : "ffffff";

        if (string.IsNullOrWhiteSpace(petName))
        {
            petName = "Pet";
        }

        into.AddPet(
            new PetCreateRequest
            {
                Name = petName,
                Type = petType,
                Race = race,
                Color = color,
                Gender = AvatarGenderType.Male,
            }
        );
    }
}
