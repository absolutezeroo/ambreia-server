using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Orleans.Snapshots.Navigator;

namespace Vortex.Primitives.Players.Grains;

/// <summary>What the navigator remembers per player: the window's place and the home room.</summary>
public interface IPlayerNavigatorGrain : IGrainWithIntegerKey
{
    Task<NavigatorWindowPreferencesSnapshot> GetWindowPreferencesAsync(CancellationToken ct);
    Task UpdateWindowPreferencesAsync(
        int x,
        int y,
        int width,
        int height,
        bool leftPaneHidden,
        int resultsMode,
        CancellationToken ct
    );

    Task<int> GetHomeRoomIdAsync(CancellationToken ct);
    Task SetHomeRoomIdAsync(int roomId, CancellationToken ct);
}
