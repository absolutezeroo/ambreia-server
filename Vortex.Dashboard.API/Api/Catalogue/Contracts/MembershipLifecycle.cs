using System.Collections.Generic;

namespace Vortex.Dashboard.API.Api.Catalogue.Contracts;

/// <summary>What happened to subscriptions during the window, from the audit trail.</summary>
public sealed record MembershipLifecycle(
    MembershipLifecycleTotals Totals,
    IReadOnlyList<MembershipMonthsBreakdown> ByMonths,
    IReadOnlyList<MembershipEventRow> RecentEvents,
    IReadOnlyList<MembershipLifecyclePoint> Timeline
);
