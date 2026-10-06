using System;
using System.Collections.Frozen;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Vortex.Dashboard.API.Hosting;
using Vortex.Dashboard.API.Security;
using Vortex.Observability.Configuration;
using Vortex.Primitives.Authentication;

namespace Vortex.Dashboard.API.Security;

/// <summary>
///     Marks a route as one that a valid login is not, by itself, enough to run.
/// </summary>
/// <remarks>
///     Metadata rather than only a filter, and for the reason
///     <c>DashboardOperationReasonTests</c> spells out: an endpoint filter is compiled into the
///     request delegate and published nowhere, so nothing can enumerate which routes carry one. The
///     marker makes the list readable — by a test, and by anyone asking what this dashboard considers
///     critical.
/// </remarks>
internal sealed class StepUpRequired
{
    public static readonly StepUpRequired Instance = new();

    private StepUpRequired() { }

    /// <summary>
    ///     What this dashboard treats as critical, named by capability rather than by route.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     By capability because that is the durable fact. A route list has to be remembered every
    ///     time somebody adds an endpoint, which is the exact failure the dashboard capability
    ///     checklist exists to catch and still only catches with a hook; a capability list is
    ///     consulted by <c>MapPost</c> itself, so a new route that grants currency is protected on the
    ///     day it is written without anyone deciding to protect it.
    ///     </para>
    ///     <para>
    ///     The four kinds here are: it mints value (currency, items), it changes who holds
    ///     power (the staff roster, which is also where a second factor is cleared for someone else),
    ///     it reaches past the hotel (a console command, a server restart, a database backup that
    ///     leaves with every row in it), or it destroys the trail (a forensics purge, the config that
    ///     could switch the protections off).
    ///     </para>
    ///     <para>
    ///     <c>OpsServerControl</c> guards no route today — it is a per-command capability inside the
    ///     console, and the console's own route is on this list, so <c>quit</c> is already covered. It
    ///     is named anyway: the day it does gate a route, that route arrives protected.
    ///     </para>
    ///     <para>
    ///     Bans, mutes and kicks are deliberately absent. They are reversible, they are what a
    ///     moderator does under time pressure, and a code prompt between a moderator and a raid in
    ///     progress buys nothing an audit row does not already give.
    ///     </para>
    /// </remarks>
    public static readonly FrozenSet<string> Capabilities = new[]
    {
        Vortex.Primitives.Permissions.Capabilities.Dashboard.OpsGrantCurrency,
        Vortex.Primitives.Permissions.Capabilities.Dashboard.OpsGrantItem,
        Vortex.Primitives.Permissions.Capabilities.Dashboard.OpsStaffManage,
        Vortex.Primitives.Permissions.Capabilities.Dashboard.OpsServerConsole,
        Vortex.Primitives.Permissions.Capabilities.Dashboard.OpsServerControl,
        Vortex.Primitives.Permissions.Capabilities.Dashboard.OpsDatabaseBackup,
        Vortex.Primitives.Permissions.Capabilities.Dashboard.OpsConfigManage,
        Vortex.Primitives.Permissions.Capabilities.Dashboard.OpsForensicsPurge,
    }.ToFrozenSet(StringComparer.Ordinal);
}

/// <summary>
///     Refuses a critical operation unless this session has proved a second factor recently.
/// </summary>
/// <remarks>
///     <para>
///     The frozen note's rule is that step-up belongs to the operator's security context and not to
///     the business payload the browser sends. So nothing here reads the request body: the decision is
///     made from the session cookie, the session's own step-up stamp, and the clock. A browser cannot
///     assert that it stepped up, because there is no field in which to say so.
///     </para>
///     <para>
///     Three outcomes, and they are told apart on purpose. An operator who has simply not stepped up
///     yet gets <c>mfa_step_up_required</c> and the client opens the code dialog. An operator with no
///     second factor at all gets <c>mfa_enrolment_required</c> — no dialog will help them, they have
///     to enrol — and answering the first code to that would leave them retrying a prompt that can
///     never succeed. A request with no live session gets 401, which is the authentication layer's
///     answer and not this one's.
///     </para>
/// </remarks>
internal sealed class DashboardStepUpFilter(
    DashboardSessionStore sessions,
    IAccountMfaService mfa,
    IOptions<ObservabilityConfig> options
) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next
    )
    {
        int minutes = options.Value.DashboardStepUpMinutes;

        if (minutes <= 0)
        {
            return await next(context).ConfigureAwait(false);
        }

        HttpContext ctx = context.HttpContext;
        DashboardPrincipal? principal = ctx.GetDashboardPrincipal();

        if (principal is null)
        {
            return Results.Json(
                new { error = "unauthenticated" },
                statusCode: StatusCodes.Status401Unauthorized
            );
        }

        string? sessionId = ctx.Request.Cookies[DashboardAuthenticationHandler.SessionCookieName];
        DateTime? steppedUpAt = sessions.SteppedUpAtUtc(sessionId);

        if (
            steppedUpAt is not null
            && DateTime.UtcNow - steppedUpAt.Value < TimeSpan.FromMinutes(minutes)
        )
        {
            return await next(context).ConfigureAwait(false);
        }

        // Only asked once the window has already failed: an operator who stepped up a minute ago is
        // waved through without a round trip to the factor store.
        bool enrolled = await mfa.IsEnabledAsync(principal.AccountId, ctx.RequestAborted)
            .ConfigureAwait(false);

        return Results.Json(
            new { error = enrolled ? "mfa_step_up_required" : "mfa_enrolment_required" },
            statusCode: StatusCodes.Status403Forbidden
        );
    }
}
