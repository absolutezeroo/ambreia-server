using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Messages.Registry;
using Vortex.Primitives.Catalog;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Snapshots.Catalog;
using Vortex.Protocol.Messages.Incoming.Catalog;
using Vortex.Protocol.Messages.Outgoing.Catalog;

namespace Vortex.PacketHandlers.Catalog;

public class GetGiftWrappingConfigurationMessageHandler(
    IFurnitureDefinitionProvider furnitureDefinitionProvider
) : IMessageHandler<GetGiftWrappingConfigurationMessage>
{
    private readonly IFurnitureDefinitionProvider _furnitureDefinitionProvider =
        furnitureDefinitionProvider;

    public async ValueTask HandleAsync(
        GetGiftWrappingConfigurationMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        ImmutableArray<int> stuffTypes = GiftWrapping.ResolveStuffTypes(
            _furnitureDefinitionProvider
        );

        await ctx.SendComposerAsync(
                new GiftWrappingConfigurationEventMessageComposer
                {
                    Configuration = new GiftWrappingConfigurationSnapshot(
                        // Off when this hotel ships no parcels at all: with an empty stuff type
                        // list the dialog would open on a selection that does not exist.
                        IsWrappingEnabled: stuffTypes.Length > 0,
                        // Wrapping is free here, so the client is told so. The official server
                        // charges one credit; that is a hotel decision, not a protocol one.
                        WrappingPrice: 0,
                        StuffTypes: [.. stuffTypes],
                        BoxTypes: [.. GiftWrapping.BoxTypes],
                        RibbonTypes: [.. GiftWrapping.RibbonTypes],
                        // The dialog appends one "default" box to the box row whether or not this
                        // list has anything in it -- an empty list leaves an uninitialised zero
                        // there, which draws a blank tile that also collides with box type 0. So
                        // it gets the plain parcel: we ship no seasonal ones to surprise anybody
                        // with, and a real entry is what keeps the row honest.
                        DefaultStuffTypes: stuffTypes.Length > 0 ? [stuffTypes[0]] : []
                    ),
                },
                ct
            )
            .ConfigureAwait(false);
    }
}
