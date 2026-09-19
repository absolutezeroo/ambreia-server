using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Protocol.Messages.Outgoing.Availability;

namespace Vortex.Main.Hosting;

/// <summary>
/// <inheritdoc cref="IMaintenanceAnnouncer" path="/summary"/>
/// </summary>
/// <remarks>
/// <para>
/// <c>MaintenanceStatus</c> (header 1737) and not <c>InfoHotelClosing</c> (184). The client renders
/// each through a different served text and only one of them describes a restart:
/// <c>opening.hours.shutdown</c> ends "welcome back tomorrow", which is a nightly closure, while
/// <c>maintenance.shutdown</c> ends "we'll be right back in approximately %d% minutes", which is
/// this. <c>%m%</c> is the minutes, <c>%d%</c> the downtime.
/// </para>
/// <para>
/// One alert per call. <c>HabboAlertDialogManager.handleHotelMaintenanceMessage</c> pops a fresh
/// <c>simpleAlert</c> every time and never counts down on its own, so a per-minute tick would be
/// five popups in five minutes rather than one that ticks.
/// </para>
/// </remarks>
internal sealed class MaintenanceAnnouncer(
    ISessionGateway sessions,
    IGrainFactory grainFactory,
    ILogger<MaintenanceAnnouncer> logger
) : IMaintenanceAnnouncer
{
    private readonly ISessionGateway _sessions = sessions;
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly ILogger<MaintenanceAnnouncer> _logger = logger;

    public async Task<int> AnnounceAsync(int minutes, int downtimeMinutes, CancellationToken ct)
    {
        // isInMaintenance stays false: the hotel is still open, and the client's notification
        // handler reads only the two numbers anyway.
        MaintenanceStatusMessageComposer composer = new()
        {
            IsInMaintenance = false,
            MinutesUntilMaintenance = minutes,
            DurationMinutes = downtimeMinutes,
        };

        int reached = 0;

        foreach (PlayerId playerId in _sessions.GetOnlinePlayerIds())
        {
            try
            {
                await _grainFactory
                    .GetPlayerPresenceGrain(playerId)
                    .SendComposerAsync(composer)
                    .ConfigureAwait(false);

                reached++;
            }
            catch (Exception ex)
            {
                // One player disconnecting mid-sweep must not cost the rest their warning. The
                // cancellation token is deliberately not checked in here either: this runs during
                // shutdown, where the token is the thing that is about to fire, and half a hotel
                // warned is worse than a sweep that takes another few milliseconds.
                _logger.LogDebug(ex, "Could not warn player {PlayerId} of the restart", playerId);
            }
        }

        return reached;
    }
}
