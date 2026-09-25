using System.Threading;
using System.Threading.Tasks;
using Vortex.Messages.Registry;
using Vortex.Primitives.Catalog.Providers;
using Vortex.Protocol.Messages.Incoming.Catalog;
using Vortex.Protocol.Messages.Outgoing.Catalog;

namespace Vortex.PacketHandlers.Catalog;

public class GetGiftWrappingConfigurationMessageHandler(IGiftWrappingProvider giftWrappingProvider)
    : IMessageHandler<GetGiftWrappingConfigurationMessage>
{
    private readonly IGiftWrappingProvider _giftWrappingProvider = giftWrappingProvider;

    public async ValueTask HandleAsync(
        GetGiftWrappingConfigurationMessage message,
        MessageContext ctx,
        CancellationToken ct
    ) =>
        await ctx.SendComposerAsync(
                new GiftWrappingConfigurationEventMessageComposer
                {
                    Configuration = _giftWrappingProvider.Configuration,
                },
                ct
            )
            .ConfigureAwait(false);
}
