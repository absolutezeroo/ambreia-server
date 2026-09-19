using System.Collections.Immutable;
using System.IO;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Marketplace;

namespace Vortex.Revisions.Revision20260701.Parsers.Marketplace;

/// <summary>
/// Price, furniture type, then a length-prefixed array of item ids. The composer
/// (<c>unknowns/_SafePkg_1746/_SafeCls_2606.as</c>, header 3695) pushes price, type,
/// <c>refs.length</c> and then every ref, so reading a third bare int read the <em>count</em> as
/// the item id: listing one item always offered item 1, and a multi-item listing lost everything
/// past the first.
/// </summary>
internal class MakeOfferMessageParser(int maxOfferItems) : IParser
{
    public IMessageEvent Parse(IClientPacket packet)
    {
        int price = packet.PopInt();
        int furniType = packet.PopInt();
        int count = packet.PopInt();

        if (count < 0 || count > maxOfferItems)
        {
            throw new InvalidDataException(
                $"Client declared an invalid marketplace offer item count of {count} (max {maxOfferItems})."
            );
        }

        ImmutableArray<int>.Builder itemIds = ImmutableArray.CreateBuilder<int>(count);

        for (int i = 0; i < count; i++)
        {
            itemIds.Add(packet.PopInt());
        }

        return new MakeOfferMessage
        {
            Price = price,
            FurniType = furniType,
            FurnitureItemIds = itemIds.ToImmutable(),
        };
    }
}
