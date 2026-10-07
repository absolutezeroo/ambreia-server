using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Catalog.Snapshots;

namespace Vortex.Primitives.Catalog.Providers;

public interface IMembershipGiftProvider
{
    IReadOnlyList<CatalogOfferSnapshot> GetAll();
    CatalogOfferSnapshot? FindByProductCode(string productCode);
    Task ReloadAsync(CancellationToken ct);
}
