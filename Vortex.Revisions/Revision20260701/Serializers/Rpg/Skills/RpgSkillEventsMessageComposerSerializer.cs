using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Rpg.Skills;

namespace Vortex.Revisions.Revision20260701.Serializers.Rpg.Skills;

internal class RpgSkillEventsMessageComposerSerializer(int header)
    : AbstractSerializer<RpgSkillEventsMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, RpgSkillEventsMessageComposer message)
    {
        packet.WriteInteger(message.Impacts.Length);

        foreach (RpgSkillImpact impact in message.Impacts)
        {
            packet
                .WriteInteger(impact.CasterObjectId)
                .WriteString(impact.SkillId)
                .WriteInteger(impact.TargetObjectId)
                .WriteInteger(impact.X)
                .WriteInteger(impact.Y);
        }

        packet.WriteInteger(message.Outcomes.Length);

        foreach (RpgSkillOutcome outcome in message.Outcomes)
        {
            packet
                .WriteInteger(outcome.SourceObjectId)
                .WriteString(outcome.SourceId)
                .WriteInteger(outcome.TargetObjectId)
                .WriteInteger(outcome.Amount)
                .WriteInteger(outcome.Absorbed)
                .WriteString(outcome.Outcome)
                .WriteInteger(outcome.Health)
                .WriteInteger(outcome.MaxHealth)
                .WriteBoolean(outcome.Killed);
        }
    }
}
