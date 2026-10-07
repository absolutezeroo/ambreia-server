using System.Collections.Generic;
using Vortex.Dashboard.API.Api.Safety.Contracts;

namespace Vortex.Dashboard.API.Api.Catalogue.Contracts;

/// <summary>
/// Who holds a club subscription, and what happened to subscriptions over the window.
/// </summary>
/// <remarks>
/// The two halves answer different questions and cover different periods: everything above
/// <paramref name="Lifecycle"/> is the population as it stands right now, and the lifecycle is the
/// window. A window that shows no purchases is not a hotel with no subscribers.
/// </remarks>
public sealed record Memberships(
    ModerationWindow Window,
    MembershipTotals Totals,
    IReadOnlyList<MembershipTypeBreakdown> ByType,
    IReadOnlyList<ExpiringMembership> TopExpiring,
    MembershipLifecycle Lifecycle
);
