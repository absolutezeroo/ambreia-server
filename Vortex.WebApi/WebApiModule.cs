using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Authentication;
using Vortex.Primitives.Content;
using Vortex.Primitives.Hosting;
using Vortex.Primitives.Plugins;
using Vortex.WebApi.Configuration;
using Vortex.WebApi.Hosting;
using Vortex.WebApi.Notifications;
using Vortex.WebApi.Services;
using Vortex.WebApi.Session;

namespace Vortex.WebApi;

/// <summary>
/// Registers the client-facing web API: the cookie session store, the auth/player services and the
/// self-hosted ASP.NET Core (Kestrel) front controller. The HTTP app is isolated in
/// <see cref="WebApiWebHost"/>, exactly like <c>Vortex.Dashboard.API</c>.
/// </summary>
public sealed class WebApiModule : IHostPluginModule
{
    public string Key => "turbo-webapi";

    public void ConfigureServices(IServiceCollection services, HostApplicationBuilder builder)
    {
        // Validated at start so an insecure listener (cleartext off-box) or an unloadable HTTPS
        // certificate is refused before the socket opens, not discovered in production.
        services
            .AddOptions<WebApiConfig>()
            .Bind(builder.Configuration.GetSection(WebApiConfig.SECTION_NAME))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<WebApiConfig>, WebApiConfigValidator>();

        // Shared with DashboardApiModule; whichever module registers first wins.
        services.TryAddSingleton<RequiredServiceGuard>();

        services.TryAddSingleton<WebApiSessionStore>();
        services.AddSingleton<IAccountSessionRevoker>(sp =>
            sp.GetRequiredService<WebApiSessionStore>()
        );
        services.TryAddSingleton<IWebApiAuthService, WebApiAuthService>();
        services.TryAddSingleton<IWebApiPlayerService, WebApiPlayerService>();
        services.TryAddSingleton<IWebApiArticleService, WebApiArticleService>();
        services.TryAddSingleton<IWebApiProfileService, WebApiProfileService>();
        services.TryAddSingleton<IWebApiRoomService, WebApiRoomService>();
        services.TryAddSingleton<IWebApiPhotoService, WebApiPhotoService>();
        services.TryAddSingleton<IWebApiFeedService, WebApiFeedService>();
        services.TryAddSingleton<IWebApiNotificationService, WebApiNotificationService>();

        // Before the sink, which takes it: the pub/sub the open tabs listen on.
        services.TryAddSingleton<NotificationBroadcaster>();

        // The notification sink. The HANDLERS that fill it need no registration of their own —
        // PluginBootstrapper scans this assembly for IEventHandler<T> because this module is
        // registered, which is the whole reason they could live here rather than in a project of
        // their own.
        services.TryAddSingleton<INotificationSink, NotificationSink>();

        // The website's write half. Registered here rather than in the dashboard module because the
        // rules it enforces (the block vocabulary, the allowed link schemes) belong to the site, and
        // the dashboard is only one screen calling them.

        services.AddHostedService<WebApiWebHost>();
        // The admin services moved to the dashboard: authoring content is its job, not the
        // emulator's. A host without that module has no authoring path here, deliberately.
    }
}
