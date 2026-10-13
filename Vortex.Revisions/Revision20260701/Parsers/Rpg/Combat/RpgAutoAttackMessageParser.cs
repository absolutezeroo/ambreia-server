using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Rpg.Combat;

namespace Vortex.Revisions.Revision20260701.Parsers.Rpg.Combat;

internal class RpgAutoAttackMessageParser : IParser
{
    public IMessageEvent Parse(IClientPacket packet) =>
        new RpgAutoAttackMessage { Active = packet.PopBoolean() };
}
