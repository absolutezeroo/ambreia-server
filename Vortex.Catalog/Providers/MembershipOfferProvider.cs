using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Database.Entities.Catalog;
using Vortex.Primitives.Catalog;
using Vortex.Primitives.Catalog.Providers;
using Vortex.Primitives.Hosting;

namespace Vortex.Catalog.Providers;

public sealed class MembershipOfferProvider(
    IDbContextFactory<VortexDbContext> dbCtxFactory,
    ILogger<IMembershipOfferProvider> logger
) : IMembershipOfferProvider, IReferenceDataProvider
{
    private readonly IDbContextFactory<VortexDbContext> _dbCtxFactory = dbCtxFactory;
    private readonly ILogger<IMembershipOfferProvider> _logger = logger;

    public int LoadStage => 0;

    private IReadOnlyList<MembershipOffer> _offers = [];

    public IReadOnlyList<MembershipOffer> GetAll()
    {
        return _offers;
    }

    public MembershipOffer? FindById(int offerId)
    {
        foreach (MembershipOffer offer in _offers)
        {
            if (offer.OfferId == offerId)
            {
                return offer;
            }
        }

        return null;
    }

    public async Task ReloadAsync(CancellationToken ct)
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        List<MembershipOfferEntity> entities = await dbCtx
            .MembershipOffers.AsNoTracking()
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        _offers = entities
            .Select(e => new MembershipOffer
            {
                OfferId = e.Id,
                ProductCode = e.ProductCode,
                PriceCredits = e.PriceCredits,
                PriceActivityPoints = e.PriceActivityPoints,
                PriceActivityPointType = e.PriceActivityPointType,
                IsVip = e.IsVip,
                Months = e.Months,
                ExtraDays = e.ExtraDays,
                IsGiftable = e.IsGiftable,
                DaysLeftAfterPurchase = 0,
                Year = 0,
                Month = 0,
                Day = 0,
            })
            .ToList();

        _logger.LogInformation("Loaded {Count} HC club offers from database", _offers.Count);
    }
}
