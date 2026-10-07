using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SuperSocket.Server;
using SuperSocket.Server.Abstractions.Host;
using Vortex.Networking.Configuration;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Networking.Revisions;

namespace Vortex.Networking.Extensions;

public static class SuperSocketHostBuilderExtensions
{
    public static ISuperSocketHostBuilder<TPackage> UseSessionGateway<TPackage>(
        this ISuperSocketHostBuilder<TPackage> builder
    )
    {
        builder.ConfigureServices(
            delegate(HostBuilderContext hostCtx, IServiceCollection services)
            {
                services.AddSingleton(sp =>
                {
                    ISessionGateway gateway = sp.GetRequiredService<ISessionGateway>();
                    IRevisionManager revisionManager = sp.GetRequiredService<IRevisionManager>();

                    return new SessionHandlers
                    {
                        Connected = async session =>
                        {
                            if (session is ISessionContext ctx)
                            {
                                ctx.SetRevisionId(revisionManager.DefaultRevisionId);

                                if (
                                    !await gateway
                                        .AddSessionAsync(ctx.SessionKey, ctx)
                                        .ConfigureAwait(false)
                                )
                                {
                                    // Over a session cap (SEC-10). The gateway registered nothing,
                                    // so closing the transport is the whole of the cleanup.
                                    await ctx.CloseSessionAsync().ConfigureAwait(false);
                                }
                            }
                        },
                        Closed = async (session, e) =>
                        {
                            if (session is ISessionContext ctx)
                            {
                                await gateway
                                    .RemoveSessionAsync(ctx.SessionKey, CancellationToken.None)
                                    .ConfigureAwait(false);
                            }
                        },
                    };
                });
            }
        );

        return builder;
    }
}
