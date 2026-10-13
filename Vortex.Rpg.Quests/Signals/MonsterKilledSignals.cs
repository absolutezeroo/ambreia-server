using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Events.Registry;
using Vortex.Primitives.Events.Combat;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Rpg.Quests;
using Vortex.Rpg.Content;
using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Quests.Signals;

/// <summary>A monster died (spec M2a §3.4): monster_killed, with its id and rank, to each participant's log.</summary>
public sealed class MonsterKilledSignals(IGrainFactory grains, ZoneKeys keys, IRpgContent content)
    : IEventHandler<MonsterKilledEvent>
{
    public async ValueTask HandleAsync(MonsterKilledEvent e, EventContext ctx, CancellationToken ct)
    {
        if (await keys.KeyOfAsync(e.Zone) is not { } zone)
        {
            return;
        }

        string rank = (content.Get<MonsterDefinition>(e.MonsterId)?.Rank ?? MonsterRank.Normal)
            .ToString()
            .ToLowerInvariant();
        QuestSignal signal = new(
            QuestSignals.MonsterKilled,
            zone,
            ImmutableDictionary<string, string>.Empty.Add("monster", e.MonsterId).Add("rank", rank)
        );

        // Different players' logs: told together.
        await Task.WhenAll(
            e.Participants.Select(p => grains.GetQuestLogGrain(p).CountAsync(signal))
        );
    }
}
