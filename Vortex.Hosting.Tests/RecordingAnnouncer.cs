using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Main.Hosting;

namespace Vortex.Hosting.Tests;

/// <summary>
/// An announcer that reaches <see cref="Reached"/> players and remembers what it was asked to say.
/// </summary>
/// <remarks>
/// The numbers are the whole feature: a warning that says the wrong minute is worse than none, and
/// the real announcer's only observable output is a packet. Recording the arguments is what lets the
/// two callers — the console command and the shutdown notice — be tested at all.
/// </remarks>
internal sealed class RecordingAnnouncer(int reached = 0) : IMaintenanceAnnouncer
{
    /// <summary>What every call was told, in order: the minutes of notice and the downtime.</summary>
    public List<(int Minutes, int DowntimeMinutes)> Calls { get; } = [];

    /// <summary>How many players each call claims to have reached.</summary>
    public int Reached { get; set; } = reached;

    public Task<int> AnnounceAsync(int minutes, int downtimeMinutes, CancellationToken ct)
    {
        Calls.Add((minutes, downtimeMinutes));

        return Task.FromResult(Reached);
    }
}
