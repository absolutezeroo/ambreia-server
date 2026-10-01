using System.Collections.Immutable;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Userdefinedroomevents.Wiredtrading;

namespace Vortex.Revisions.Revision20260701.Parsers.UserDefinedRoomEvents.Wiredtrading;

/// <summary>
/// The remove flag, then a counted list of item ids — the shape
/// <c>WiredTradeUpdateItemsComposer</c> writes as <c>[remove, ids.length, ...ids]</c>.
/// </summary>
internal class WiredTradeUpdateItemsMessageParser : IParser
{
    public IMessageEvent Parse(IClientPacket packet)
    {
        bool remove = packet.PopBoolean();
        int count = packet.PopCount(sizeof(int));

        ImmutableArray<int>.Builder ids = ImmutableArray.CreateBuilder<int>(count);

        for (int i = 0; i < count; i++)
        {
            ids.Add(packet.PopInt());
        }

        return new WiredTradeUpdateItemsMessage { Remove = remove, ItemIds = ids.ToImmutable() };
    }
}
