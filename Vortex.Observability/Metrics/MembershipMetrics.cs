using System;
using System.Diagnostics.Metrics;
using System.Threading;
using Vortex.Observability.Diagnostics;

namespace Vortex.Observability.Metrics;

/// <summary>
/// Exposes a live gauge of active Membership subscribers under the shared "Vortex" meter. The value
/// is pulled from a cached count refreshed off the hot path (see the refresh service), so scrapes
/// never touch the database directly.
/// </summary>
public sealed class MembershipMetrics : IDisposable
{
    private readonly Meter _meter;
    private int _activeSubscribers;

    public MembershipMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(VortexTelemetry.Name, VortexTelemetry.Version);

        _meter.CreateObservableGauge(
            "Vortex.club.active_subscribers",
            () => Volatile.Read(ref _activeSubscribers),
            unit: "{subscriber}",
            description: "Players with an active Membership / VIP membership."
        );
    }

    public void SetActiveSubscribers(int count) => Volatile.Write(ref _activeSubscribers, count);

    /// <summary>The last refreshed active-subscriber count, for surfacing on the dashboard overview.</summary>
    public int ActiveSubscribers => Volatile.Read(ref _activeSubscribers);

    public void Dispose() => _meter.Dispose();
}
