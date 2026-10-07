using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Database.Context;
using Vortex.Primitives.Authentication;
using Vortex.Primitives.Crypto;
using Vortex.Primitives.Events;
using Vortex.Primitives.Hosting;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Plugins;
using Vortex.Primitives.Plugins.Exports;
using Vortex.Primitives.Shop;
using Vortex.WebApi.Configuration;
using Vortex.WebApi.Hosting;
using Vortex.WebApi.Notifications;
using Vortex.WebApi.Services;
using Vortex.WebApi.Session;

namespace Vortex.WebApi;

/// <summary>
/// The client-facing web API: the cookie session store, the auth/player services and the
/// self-hosted ASP.NET Core (Kestrel) front controller in <see cref="WebApiWebHost"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>A LOADABLE plugin, not a compiled-in module</b> — the same conversion the dashboard went
/// through. It used to be an <c>IHostPluginModule</c> that <c>Vortex.Main</c> referenced and
/// registered at startup; as an <see cref="IVortexPlugin"/> it is a folder with a
/// <c>manifest.json</c> that PluginManager loads, and can unload and reload while the hotel runs.
/// </para>
/// <para>
/// The two rules the dashboard's remarks spell out hold here unchanged. <b>Nothing in the host may
/// reference this assembly</b>, or <c>ByteLoadingAlc</c> hands it to the default context and it can
/// never unload. And <b>nothing in the host may hold one of its objects</b>: the session store used
/// to be registered in the host as an <see cref="IAccountSessionRevoker"/>, and now goes out through
/// <see cref="BindExportsAsync"/> instead.
/// </para>
/// <para>
/// Unloading it drops every web session — they live in <see cref="WebApiSessionStore"/>, in memory —
/// and closes every notification stream. The game is untouched: the client talks to the emulator's
/// own socket, not to this.
/// </para>
/// </remarks>
public sealed class WebApiPlugin : IVortexPlugin
{
    public string Key => "turbo-webapi";

    public string Version => "1.0.0";

    public void ConfigureServices(IServiceCollection services, PluginManifest manifest)
    {
        BorrowFromHost(services);

        // Bound here from the host's configuration rather than borrowed as bound options: the host
        // no longer knows this section exists. The validator runs on the first read of `.Value`,
        // which WebApiWebHost's constructor does — an insecure listener or an unloadable certificate
        // therefore fails the plugin's activation, where it used to fail the host's start.
        services
            .AddOptions<WebApiConfig>()
            .Configure<IConfiguration>(
                (config, configuration) =>
                    configuration.GetSection(WebApiConfig.SECTION_NAME).Bind(config)
            );

        services.AddSingleton<IValidateOptions<WebApiConfig>, WebApiConfigValidator>();

        // Its own rather than the host's, as the dashboard does: a guard object with no shared state.
        // `Required` still takes the hotel down — the guard stops the borrowed host lifetime.
        services.TryAddSingleton<RequiredServiceGuard>();

        // NOT registered as IAccountSessionRevoker: see BindExportsAsync.
        services.TryAddSingleton<WebApiSessionStore>();
        services.TryAddSingleton<IWebApiAuthService, WebApiAuthService>();
        services.TryAddSingleton<IWebApiPlayerService, WebApiPlayerService>();
        services.TryAddSingleton<IWebApiArticleService, WebApiArticleService>();
        services.TryAddSingleton<IWebApiProfileService, WebApiProfileService>();
        services.TryAddSingleton<IWebApiRoomService, WebApiRoomService>();
        services.TryAddSingleton<IWebApiNotificationService, WebApiNotificationService>();
        services.TryAddSingleton<IPasswordResetMailer, SmtpPasswordResetMailer>();
        services.TryAddSingleton<IWebApiPasswordResetService, WebApiPasswordResetService>();

        // Before the sink, which takes it: the pub/sub the open tabs listen on.
        services.TryAddSingleton<NotificationBroadcaster>();

        // The notification sink. The HANDLERS that fill it need no registration of their own —
        // PluginManager scans this assembly for IEventHandler<T> and resolves them from this
        // container, and unregisters them again on unload.
        services.TryAddSingleton<INotificationSink, NotificationSink>();

        // PluginManager starts every IHostedService a plugin registers, and stops it on unload.
        services.AddHostedService<WebApiWebHost>();
    }

    /// <summary>
    /// Everything the web API needs from the hotel, declared once.
    /// </summary>
    /// <remarks>
    /// This list is the contract between the web API and the hotel: every constructor parameter of
    /// the types registered above, plus every service <c>WebApiWebHost.ForwardSingletons</c> hands
    /// the Kestrel app, that the plugin does not register itself. PluginManager checks each entry at
    /// activation, and <c>WebApiPluginContainerTests</c> fails on a miss before anything ships.
    /// </remarks>
    private static void BorrowFromHost(IServiceCollection services)
    {
        // Configuration and process.
        services.UseHostService<IConfiguration>();
        services.UseHostService<IHostEnvironment>();
        services.UseHostService<IHostApplicationLifetime>();

        // Data and the live hotel.
        services.UseHostService<IDbContextFactory<VortexDbContext>>();
        services.UseHostService<IGrainFactory>();
        services.UseHostService<IEventPublisher>();
        // The very instance the grains filter with: one compiled list, one reload.
        services.UseHostService<IWordFilter>();
        services.UseHostService<IAuditSink>();

        // Accounts.
        services.UseHostService<IAccountAuthenticator>();
        services.UseHostService<IAccountPasswordService>();
        services.UseHostService<IAccountMfaService>();
        services.UseHostService<IAccountEmailService>();
        services.UseHostService<IAccountSafetyLockService>();
        services.UseHostService<IAccountSafetyQuestionsService>();
        services.UseHostService<IAccountTrustedLocationService>();

        // The shop: a module this assembly does not reference, reached through the interface in
        // Vortex.Primitives.
        services.UseHostService<IShopService>();
    }

    /// <summary>
    /// Publishes the one thing the hotel needs back: a password change must sign the account out of
    /// the website too. Into a slot the HOST owns, refilled on every load, so the host never holds
    /// the session store itself.
    /// </summary>
    public Task BindExportsAsync(IExportBinder binder, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(binder);
        ArgumentNullException.ThrowIfNull(services);

        return binder.ExportAsync<IAccountSessionRevoker>(
            IAccountSessionRevoker.WebApiExportKey,
            services.GetRequiredService<WebApiSessionStore>()
        );
    }

    /// <summary>
    /// Nothing to do: <see cref="WebApiWebHost"/> is an <c>IHostedService</c>, and PluginManager has
    /// already started it by the time this runs.
    /// </summary>
    public Task StartAsync(IServiceProvider services, CancellationToken ct) => Task.CompletedTask;

    /// <summary>Likewise: the rollback stops the hosted service.</summary>
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Nothing of its own to release — every disposable lives in the plugin's service provider,
    /// which PluginManager disposes before it unloads the context.
    /// </summary>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
