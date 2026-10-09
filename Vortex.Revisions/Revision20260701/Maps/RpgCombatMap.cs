using Vortex.Primitives.Networking.Revisions;
using Vortex.Protocol.Messages.Outgoing.Rpg.Combat;
using Vortex.Revisions.Revision20260701.Serializers.Rpg.Combat;

namespace Vortex.Revisions.Revision20260701.Maps;

/// <summary>Combat (spec M1a §9.2): hits, health, the HUD's own vitals, death, a refused attack.</summary>
internal sealed class RpgCombatMap : IRevisionMap
{
    public void RegisterInto(IRevisionMapBuilder builder)
    {
        builder.MapSerializer(
            typeof(RpgCombatEventsMessageComposer),
            new RpgCombatEventsMessageComposerSerializer(
                MessageComposer.RpgCombatEventsMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(RpgVitalsMessageComposer),
            new RpgVitalsMessageComposerSerializer(MessageComposer.RpgVitalsMessageComposer)
        );
        builder.MapSerializer(
            typeof(RpgSelfVitalsMessageComposer),
            new RpgSelfVitalsMessageComposerSerializer(MessageComposer.RpgSelfVitalsMessageComposer)
        );
        builder.MapSerializer(
            typeof(RpgDeathMessageComposer),
            new RpgDeathMessageComposerSerializer(MessageComposer.RpgDeathMessageComposer)
        );
        builder.MapSerializer(
            typeof(RpgCombatResultMessageComposer),
            new RpgCombatResultMessageComposerSerializer(
                MessageComposer.RpgCombatResultMessageComposer
            )
        );
    }
}
