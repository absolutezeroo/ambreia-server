using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Messages.Registry;
using Vortex.Primitives.Pets.Providers;
using Vortex.Primitives.Pets.Snapshots;
using Vortex.Protocol.Messages.Incoming.Catalog;
using Vortex.Protocol.Messages.Outgoing.Catalog;

namespace Vortex.PacketHandlers.Catalog;

public class GetSellablePetPalettesMessageHandler(IPetSkinProvider petSkinProvider)
    : IMessageHandler<GetSellablePetPalettesMessage>
{
    private readonly IPetSkinProvider _petSkinProvider = petSkinProvider;

    public async ValueTask HandleAsync(
        GetSellablePetPalettesMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        int petType = ExtractPetTypeFromLocalizationId(message.LocalizationId);

        ImmutableArray<PetSkinEntry> palettes =
            petType >= 0 ? _petSkinProvider.GetSkinsForType(petType).ToImmutableArray() : [];

        await ctx.SendComposerAsync(
                new SellablePetPalettesMessageComposer
                {
                    ProductCode = message.LocalizationId,
                    Palettes = palettes,
                },
                ct
            )
            .ConfigureAwait(false);
    }

    // Mirror of Flash client getPetTypeIndexFromProduct: scan from the end for trailing digits,
    // then return the integer formed by those digits if they are preceded by a non-digit char.
    private static int ExtractPetTypeFromLocalizationId(string localizationId)
    {
        if (string.IsNullOrEmpty(localizationId))
        {
            return -1;
        }

        int i = localizationId.Length - 1;

        while (i >= 0 && char.IsDigit(localizationId[i]))
        {
            i--;
        }

        if (i > 0 && i < localizationId.Length - 1)
        {
            return int.Parse(localizationId.AsSpan(i + 1));
        }

        return -1;
    }
}
