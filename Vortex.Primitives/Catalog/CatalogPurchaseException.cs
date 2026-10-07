using System;
using Vortex.Primitives.Catalog.Enums;

namespace Vortex.Primitives.Catalog;

public sealed class CatalogPurchaseException(
    CatalogPurchaseErrorType errorType,
    CatalogBalanceFailure? balanceFailure = null,
    Exception? innerException = null
) : Exception($"Catalog purchase failed with error '{errorType}'.", innerException)
{
    public CatalogPurchaseErrorType ErrorType { get; } = errorType;

    public CatalogBalanceFailure? BalanceFailure { get; } = balanceFailure;
}
