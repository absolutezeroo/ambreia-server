using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Main.Hosting;

/// <summary>
/// Tells every connected player that the hotel is about to restart.
/// </summary>
/// <remarks>
/// Two callers, which is why this is a service and not a loop inside one of them: the operator's
/// <c>announce</c> console command, and the shutdown notice the hotel gives itself when something
/// stops the container.
/// </remarks>
public interface IMaintenanceAnnouncer
{
    /// <summary>
    /// Warns everyone online that the hotel restarts in <paramref name="minutes"/> and expects to be
    /// back after <paramref name="downtimeMinutes"/>.
    /// </summary>
    /// <returns>How many players were actually reached.</returns>
    Task<int> AnnounceAsync(int minutes, int downtimeMinutes, CancellationToken ct);
}
