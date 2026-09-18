using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Database.Entities.Marketplace;
using Vortex.Primitives.Hosting;
using Vortex.Primitives.Marketplace.Providers;
using Vortex.Primitives.Marketplace.Snapshots;

namespace Vortex.Marketplace.Providers;

public sealed class MarketplaceSettingsProvider(
    IDbContextFactory<VortexDbContext> dbCtxFactory,
    ILogger<MarketplaceSettingsProvider> logger
) : IMarketplaceSettingsProvider, IReferenceDataProvider
{
    private static readonly MarketplaceSettingsSnapshot Defaults = new()
    {
        CommissionPercent = 1,
        OfferDurationSeconds = 259200,
        AveragePricePeriodDays = 7,
        HalfTaxLimit = 1_000_000_000,
    };

    public int LoadStage => 0;

    private readonly IDbContextFactory<VortexDbContext> _dbCtxFactory = dbCtxFactory;
    private readonly ILogger<MarketplaceSettingsProvider> _logger = logger;

    // Single reference swap on reload - never mutated in place, so concurrent reads during a
    // reload always see either the old or the new snapshot, never a partially-updated one.
    private volatile MarketplaceSettingsSnapshot _settings = Defaults;

    public MarketplaceSettingsSnapshot GetSettings() => _settings;

    public async Task ReloadAsync(CancellationToken ct)
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        MarketplaceSettingsEntity? entity = await dbCtx
            .MarketplaceSettings.AsNoTracking()
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            _logger.LogWarning(
                "No marketplace_settings row found; falling back to built-in defaults "
                    + "(commission={CommissionPercent}%, offerDuration={OfferDurationSeconds}s)",
                Defaults.CommissionPercent,
                Defaults.OfferDurationSeconds
            );

            _settings = Defaults;

            return;
        }

        _settings = new MarketplaceSettingsSnapshot
        {
            CommissionPercent = entity.CommissionPercent,
            OfferDurationSeconds = entity.OfferDurationSeconds,
            AveragePricePeriodDays = entity.AveragePricePeriodDays,
            // Never let a zero reach the wire: the client divides by this to preview the selling
            // fee, so a misconfigured row would blank the fee for every listing.
            HalfTaxLimit = entity.HalfTaxLimit > 0 ? entity.HalfTaxLimit : Defaults.HalfTaxLimit,
        };
    }
}
