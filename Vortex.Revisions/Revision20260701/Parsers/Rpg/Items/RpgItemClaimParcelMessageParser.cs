using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Rpg.Items;

namespace Vortex.Revisions.Revision20260701.Parsers.Rpg.Items;

internal class RpgItemClaimParcelMessageParser : IParser
{
    public IMessageEvent Parse(IClientPacket packet) =>
        new RpgItemClaimParcelMessage { Parcel = packet.PopInt() };
}
