using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Rpg.Skills;

namespace Vortex.Revisions.Revision20260701.Parsers.Rpg.Skills;

internal class RpgSkillCastMessageParser : IParser
{
    public IMessageEvent Parse(IClientPacket packet) =>
        new RpgSkillCastMessage
        {
            SkillId = packet.PopString(),
            TargetObjectId = packet.PopInt(),
            X = packet.PopInt(),
            Y = packet.PopInt(),
        };
}
