using System;
using System.Collections.Generic;
using Vortex.Dashboard.API.Hosting;
using Vortex.Primitives.Catalog.Enums;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Furniture.StuffData;
using Vortex.Primitives.Rooms.Enums;

namespace Vortex.Dashboard.API.Operations.Catalogue.Contracts;

public sealed record UpdateCatalogOfferRequest(
    int OfferId,
    string LocalizationId,
    int CostCredits,
    int CostCurrency,
    int? CurrencyTypeId,
    bool CanGift,
    bool CanBundle,
    int MembershipLevel,
    int DiscountPercent,
    bool Visible,
    string Reason
) : IReasonedRequest;
