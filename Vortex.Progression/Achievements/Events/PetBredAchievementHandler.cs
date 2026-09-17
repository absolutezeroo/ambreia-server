using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Orleans;

namespace Vortex.Progression.Achievements.Events;

/// <summary>
/// The one achievement trigger that is not a vocabulary mapping, for the same reason the quest side
/// has one: "owning %limit% different coloured baby bears" counts a property of the pets table, not
/// acts a signal could carry.
/// </summary>
/// <remarks>
/// A <c>ProgressSignal</c> says what a player just did, and content filters on its facts. "How many
/// colours do you now own" is neither — it is a number only the pets table knows, and putting it on
/// the signal to serve five achievements would put a database read in every translator. So this
/// stays a handler on the event, and the recount happens in the grain that owns the query.
/// </remarks>
public sealed class PetBredAchievementHandler(IGrainFactory grainFactory)
    : IEventHandler<PetBredEvent>
{
    public async ValueTask HandleAsync(PetBredEvent e, EventContext ctx, CancellationToken ct) =>
        await grainFactory
            .GetPlayerAchievementGrain(e.OwnerId.Value)
            .ProgressBabyPetColoursAsync(e.Type, ct)
            .ConfigureAwait(false);
}
