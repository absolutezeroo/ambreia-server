using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Primitives.Catalog.Providers;

public interface IMembershipOfferProvider
{
    IReadOnlyList<MembershipOffer> GetAll();
    MembershipOffer? FindById(int offerId);
    Task ReloadAsync(CancellationToken ct);
}
