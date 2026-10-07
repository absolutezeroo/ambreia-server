using System.Collections.Generic;
using Vortex.Observability.Runtime;

namespace Vortex.Dashboard.API.Api.Platform.Contracts;

/// <summary>
/// The hotel in one answer: health, incidents, live traffic and the totals behind them.
/// </summary>
/// <param name="Status">The overall health word, repeated from <paramref name="Health"/> so the
/// status pill can be drawn without reading into the snapshot.</param>
/// <param name="ActiveRooms">Rooms the directory grain is holding, not rows in the table.</param>
public sealed record DashboardOverview(
    string Status,
    InfrastructureHealthSnapshot Health,
    long UptimeSeconds,
    long ManagedMemoryMb,
    int ActiveSessions,
    int ActiveRooms,
    int ActiveMembershipSubscribers,
    IncidentDetectionSnapshot Incidents,
    OverviewLive Live,
    IReadOnlyList<AuditCategoryCount> AuditLastHourByCategory,
    OverviewTotals Totals
);
