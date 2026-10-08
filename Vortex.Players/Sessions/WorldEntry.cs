using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Sessions;

namespace Vortex.Players.Sessions;

/// <inheritdoc cref="IWorldEntry"/>
internal sealed class WorldEntry(ISessionGateway sessionGateway, IGrainFactory grainFactory)
    : IWorldEntry
{
    public async Task EnterAsync(SessionKey key, PlayerId playerId, CancellationToken ct)
    {
        await sessionGateway.AddSessionToPlayerAsync(key, playerId, ct).ConfigureAwait(false);
        await grainFactory
            .GetPlayerPresenceGrain(playerId)
            .EnterWorldAsync(ct)
            .ConfigureAwait(false);
    }
}
