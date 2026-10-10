using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Vortex.Primitives.Players;
using Vortex.Primitives.World.Layers;
using Vortex.Primitives.World.Rules;
using Vortex.Rpg.Zones.Rules;

namespace Vortex.Rooms.Tests.Support;

/// <summary>The quest log's source: read from a dictionary, a changed state raises QuestState on the
/// quest's id.</summary>
internal sealed class TestQuestSource(Dictionary<PlayerId, TestQuestFacts> logs) : IZoneFactsSource
{
    public Type FactsType => typeof(TestQuestFacts);

    public Task<ZoneFacts?> ReadAsync(PlayerId player) =>
        Task.FromResult<ZoneFacts?>(logs.GetValueOrDefault(player));

    public IEnumerable<ZoneTriggerSignal> Changes(ZoneFacts before, ZoneFacts after)
    {
        TestQuestFacts was = (TestQuestFacts)before;

        foreach ((string quest, string state) in ((TestQuestFacts)after).States)
        {
            if (was.States.GetValueOrDefault(quest) != state)
            {
                yield return new ZoneTriggerSignal(ZoneTriggerType.QuestState, quest);
            }
        }
    }
}
