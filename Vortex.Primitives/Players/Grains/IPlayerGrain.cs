using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Rooms.Enums;

namespace Vortex.Primitives.Players.Grains;

/// <summary>A player's card: name, motto, look. Everything else left with the coupe au noyau.</summary>
public interface IPlayerGrain : IGrainWithIntegerKey
{
    public Task SetFigureAsync(string figure, AvatarGenderType gender, CancellationToken ct);

    public Task SetNameAsync(string name, CancellationToken ct);

    public Task SetMottoAsync(string text, CancellationToken ct);

    public Task<PlayerSummarySnapshot> GetSummaryAsync(CancellationToken ct);
}
