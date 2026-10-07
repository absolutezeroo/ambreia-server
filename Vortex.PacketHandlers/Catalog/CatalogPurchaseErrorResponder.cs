using System.Threading;
using System.Threading.Tasks;
using Vortex.Messages.Registry;
using Vortex.Primitives.Catalog;
using Vortex.Protocol.Messages.Outgoing.Catalog;

namespace Vortex.PacketHandlers.Catalog;

/// <summary>
/// Turns a <see cref="CatalogPurchaseException"/> into the response the client expects. Shared by
/// every purchase entry point so a new one cannot quietly answer a refused purchase differently --
/// the client shows nothing at all when it gets neither composer.
/// </summary>
internal static class CatalogPurchaseErrorResponder
{
    public static async ValueTask SendAsync(
        MessageContext ctx,
        CatalogPurchaseException ex,
        CancellationToken ct
    )
    {
        if (ex.BalanceFailure is not null)
        {
            await ctx.SendComposerAsync(
                    new NotEnoughBalanceMessageComposer
                    {
                        NotEnoughCredits = ex.BalanceFailure.NotEnoughCredits,
                        NotEnoughActivityPoints = ex.BalanceFailure.NotEnoughActivityPoints,
                        ActivityPointType = ex.BalanceFailure.ActivityPointType,
                    },
                    ct
                )
                .ConfigureAwait(false);

            return;
        }

        await ctx.SendComposerAsync(
                new PurchaseNotAllowedMessageComposer { ErrorType = ex.ErrorType },
                ct
            )
            .ConfigureAwait(false);
    }
}
