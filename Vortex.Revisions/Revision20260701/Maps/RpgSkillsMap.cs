using Vortex.Primitives.Networking.Revisions;
using Vortex.Protocol.Messages.Outgoing.Rpg.Skills;
using Vortex.Revisions.Revision20260701.Parsers.Rpg.Skills;
using Vortex.Revisions.Revision20260701.Serializers.Rpg.Skills;

namespace Vortex.Revisions.Revision20260701.Maps;

/// <summary>Skills and statuses (spec M1b §10): a cast, the bar, a cancel; the skills, refusals,
/// casts starting and ending, recharges, statuses and what skills did.</summary>
internal sealed class RpgSkillsMap : IRevisionMap
{
    public void RegisterInto(IRevisionMapBuilder builder)
    {
        builder.MapParser(MessageEvent.RpgSkillCastMessageEvent, new RpgSkillCastMessageParser());
        builder.MapParser(
            MessageEvent.RpgActionBarSetMessageEvent,
            new RpgActionBarSetMessageParser()
        );
        builder.MapParser(MessageEvent.RpgCastCancelMessageEvent, new RpgCastCancelMessageParser());

        builder.MapSerializer(
            typeof(RpgSkillsMessageComposer),
            new RpgSkillsMessageComposerSerializer(MessageComposer.RpgSkillsMessageComposer)
        );
        builder.MapSerializer(
            typeof(RpgSkillResultMessageComposer),
            new RpgSkillResultMessageComposerSerializer(
                MessageComposer.RpgSkillResultMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(RpgCastStartMessageComposer),
            new RpgCastStartMessageComposerSerializer(MessageComposer.RpgCastStartMessageComposer)
        );
        builder.MapSerializer(
            typeof(RpgCastEndMessageComposer),
            new RpgCastEndMessageComposerSerializer(MessageComposer.RpgCastEndMessageComposer)
        );
        builder.MapSerializer(
            typeof(RpgCooldownsMessageComposer),
            new RpgCooldownsMessageComposerSerializer(MessageComposer.RpgCooldownsMessageComposer)
        );
        builder.MapSerializer(
            typeof(RpgStatusesMessageComposer),
            new RpgStatusesMessageComposerSerializer(MessageComposer.RpgStatusesMessageComposer)
        );
        builder.MapSerializer(
            typeof(RpgSkillEventsMessageComposer),
            new RpgSkillEventsMessageComposerSerializer(
                MessageComposer.RpgSkillEventsMessageComposer
            )
        );
    }
}
