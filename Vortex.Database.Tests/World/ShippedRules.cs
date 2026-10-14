using System;
using System.Collections.Generic;
using System.Linq;
using Vortex.Primitives.World.Layers;
using Vortex.Primitives.World.Rules;

namespace Vortex.Database.Tests.World;

/// <summary>
/// What the shipped modules declare to the editor's validator (spec Z1 §9): the rooms' own rules,
/// the characters' and the monsters' — by hand here, as the host gathers them from its modules.
/// </summary>
internal static class ShippedRules
{
    public static IReadOnlyList<IZoneRuleDeclaration> Rules { get; } =
    [
        .. new Type[]
        {
            typeof(TimeOfDayCondition),
            typeof(ZoneActiveCondition),
            typeof(ShowDialogueAction),
            typeof(ActivateZoneAction),
            typeof(DeactivateZoneAction),
            typeof(TeleportAction),
            typeof(OpenDoorAction),
            typeof(PlaySoundAction),
            typeof(PlayerLevelCondition),
            typeof(HasItemCondition),
            typeof(SpawnGroupAction),
            typeof(QuestStateCondition),
            typeof(StartQuestAction),
        }.Select(t => new DeclaredZoneRule(t)),
    ];

    public static IReadOnlyList<IZoneTriggerSource> Triggers { get; } =
    [
        new DeclaredZoneTriggers([
            ZoneTriggerType.PlayerLevel,
            ZoneTriggerType.ItemOwned,
            ZoneTriggerType.MonsterKilled,
            ZoneTriggerType.QuestState,
        ]),
    ];
}
