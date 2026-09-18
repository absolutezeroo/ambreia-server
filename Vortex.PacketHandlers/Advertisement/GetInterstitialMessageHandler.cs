using System.Threading;
using System.Threading.Tasks;
using Vortex.Messages.Registry;
using Vortex.Protocol.Messages.Incoming.Advertisement;
using Vortex.Protocol.Messages.Outgoing.Advertisement;

namespace Vortex.PacketHandlers.Advertisement;

/// <summary>
/// Whether an interstitial ad may be shown. Asked on every room entry; answered no.
/// </summary>
/// <remarks>
/// An official hotel answers this on entry too, and answers false — there is no ad to show. It is
/// the answer itself that matters rather than its value: <c>AdManager</c> is what asked, and leaving
/// it unanswered leaves it waiting on every room change instead of having been told no once.
/// Hosting real interstitials would make this a lookup; until there are any, no is the truth.
/// </remarks>
public class GetInterstitialMessageHandler : IMessageHandler<GetInterstitialMessage>
{
    public async ValueTask HandleAsync(
        GetInterstitialMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        await ctx.SendComposerAsync(
                new InterstitialMessageComposer { CanShowInterstitial = false },
                ct
            )
            .ConfigureAwait(false);
    }
}
