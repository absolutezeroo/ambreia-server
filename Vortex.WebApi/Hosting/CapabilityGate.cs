using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Vortex.Primitives.Permissions;
using Vortex.WebApi.Http.Errors;
using Vortex.WebApi.Session;

namespace Vortex.WebApi.Hosting;

/// <summary>
/// A session first, then a capability: 401 without the one, 403 without the other. One gate for
/// every staff route, declared on the route (<see cref="RequireCapability{TBuilder}"/>) rather than
/// repeated as a preamble in each handler.
/// </summary>
internal static class CapabilityGate
{
    /// <summary>Refuses the route unless the session's account holds <paramref name="capability"/>.</summary>
    public static TBuilder RequireCapability<TBuilder>(this TBuilder builder, string capability)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(
            async (context, next) =>
                await DenyAsync(context.HttpContext, capability, context.HttpContext.RequestAborted)
                    .ConfigureAwait(false)
                ?? await next(context).ConfigureAwait(false)
        );

    /// <summary>
    /// The gate itself, for the one route whose capability depends on the request (taking someone
    /// else's lock): 401 without a session, 403 without the capability, null when allowed.
    /// </summary>
    public static async Task<IResult?> DenyAsync(
        HttpContext ctx,
        string capability,
        CancellationToken ct
    )
    {
        int? accountId = ctx.AccountId(
            ctx.RequestServices.GetRequiredService<WebApiSessionStore>()
        );

        if (accountId is null)
        {
            return new UnauthorizedError("unauthorized");
        }

        PermissionSet granted = await ctx
            .RequestServices.GetRequiredService<IPermissionService>()
            .ResolveForAccountAsync(accountId.Value, ct)
            .ConfigureAwait(false);

        return granted.Has(capability) ? null : new ForbiddenError("editor.forbidden");
    }
}
