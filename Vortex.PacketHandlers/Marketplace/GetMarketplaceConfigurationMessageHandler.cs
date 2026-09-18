using System.Threading;
using System.Threading.Tasks;
using Vortex.Messages.Registry;
using Vortex.Primitives.Marketplace.Providers;
using Vortex.Primitives.Marketplace.Snapshots;
using Vortex.Protocol.Messages.Incoming.Marketplace;
using Vortex.Protocol.Messages.Outgoing.Marketplace;

namespace Vortex.PacketHandlers.Marketplace;

public class GetMarketplaceConfigurationMessageHandler(
    IMarketplaceSettingsProvider settingsProvider
) : IMessageHandler<GetMarketplaceConfigurationMessage>
{
    private const int SecondsPerHour = 60 * 60;

    private readonly IMarketplaceSettingsProvider _settingsProvider = settingsProvider;

    public async ValueTask HandleAsync(
        GetMarketplaceConfigurationMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        MarketplaceSettingsSnapshot settings = _settingsProvider.GetSettings();

        await ctx.SendComposerAsync(
                new MarketplaceConfigurationEventMessageComposer
                {
                    Enabled = true,
                    Commission = settings.CommissionPercent,
                    // Habbo prices credits in batches here; we sell none, and the client hides the
                    // batch entirely when the size is zero.
                    TokenBatchPrice = 0,
                    TokenBatchSize = 0,
                    OfferMinPrice = 1,
                    OfferMaxPrice = 9999999,
                    ExpirationHours = ToExpirationHours(settings.OfferDurationSeconds),
                    AveragePricePeriod = settings.AveragePricePeriodDays,
                    // The fee the client previews has to be the fee the purchase grain takes, and
                    // that one is a flat commission -- so the same number goes in both fields.
                    SellingFeePercentage = settings.CommissionPercent,
                    // Assigned into the client's marketplace model and never read back in this
                    // build; left at zero rather than invented.
                    RevenueLimit = 0,
                    HalfTaxLimit = settings.HalfTaxLimit,
                },
                ct
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// An offer's lifetime as the client counts it. It is stored in seconds because that is the
    /// unit the purchase grain expires an offer on, and read in hours because that is the unit the
    /// client divides by 24 to say how many days a listing lasts — sending the seconds straight
    /// through is what used to put "10800 days" in the dialog.
    /// </summary>
    /// <remarks>Rounded up, so a duration shorter than an hour still reads as one rather than as
    /// an offer that has already expired.</remarks>
    public static int ToExpirationHours(int offerDurationSeconds) =>
        offerDurationSeconds <= 0
            ? 0
            : (offerDurationSeconds + SecondsPerHour - 1) / SecondsPerHour;
}
