using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rpg.Quests;
using Vortex.Primitives.World.Layers;
using Vortex.Primitives.World.Rules;
using Vortex.Rpg.Zones.Rules;

namespace Vortex.Rpg.Quests.Zones;

/// <summary>
/// The quest log's part of a zone's facts (spec M2a §3.3, Z1 §4.3): read on entry, pushed by the log
/// on each change; a quest whose state moved raises QuestState on its id.
/// </summary>
public sealed class QuestFactsSource(IGrainFactory grains) : IZoneFactsSource
{
    public Type FactsType => typeof(QuestZoneFacts);

    public IReadOnlyCollection<ZoneTriggerType> Raises { get; } = [ZoneTriggerType.QuestState];

    public async Task<ZoneFacts?> ReadAsync(PlayerId player) =>
        await grains.GetQuestLogGrain(player).GetZoneFactsAsync();

    public IEnumerable<ZoneTriggerSignal> Changes(ZoneFacts before, ZoneFacts after)
    {
        if (before is not QuestZoneFacts was || after is not QuestZoneFacts now)
        {
            yield break;
        }

        foreach ((string quest, string state) in now.States)
        {
            if (was.States.GetValueOrDefault(quest) != state)
            {
                yield return new ZoneTriggerSignal(ZoneTriggerType.QuestState, quest);
            }
        }
    }
}
