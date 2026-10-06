using System;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Dashboard.API.Admin.Catalogue;
using Vortex.Dashboard.API.Admin.Hotel;
using Vortex.Dashboard.API.Admin.Progression;
using Vortex.Dashboard.API.Api;
using Vortex.Dashboard.API.Api.Catalogue;
using Vortex.Dashboard.API.Api.Hotel;
using Vortex.Dashboard.API.Api.Platform;
using Vortex.Dashboard.API.Api.Progression;
using Vortex.Dashboard.API.Api.Safety;
using Vortex.Dashboard.API.Hosting;
using Vortex.Dashboard.API.Http;
using Vortex.Dashboard.API.Infrastructure;
using Vortex.Dashboard.API.Operations;
using Vortex.Dashboard.API.Operations.Catalogue;
using Vortex.Dashboard.API.Operations.Hotel;
using Vortex.Dashboard.API.Operations.Platform;
using Vortex.Dashboard.API.Operations.Progression;
using Vortex.Dashboard.API.Operations.Safety;
using Vortex.Dashboard.API.Security;
using Vortex.Database.Backup;
using Vortex.Database.Context;
using Vortex.Observability.Configuration;
using Vortex.Observability.Metrics;
using Vortex.Observability.Runtime;
using Vortex.Primitives.Authentication;
using Vortex.Primitives.Catalog;
using Vortex.Primitives.Catalog.Providers;
using Vortex.Primitives.Catalog.Tags;
using Vortex.Primitives.Console;
using Vortex.Primitives.Content;
using Vortex.Primitives.Crypto;
using Vortex.Primitives.Feed;
using Vortex.Primitives.Fishing;
using Vortex.Primitives.Furniture;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Hosting;
using Vortex.Primitives.Moderation;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Navigator;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Players.Providers;
using Vortex.Primitives.Plugins;
using Vortex.Primitives.Plugins.Exports;
using Vortex.Primitives.Prizes;
using Vortex.Primitives.Quests;
using Vortex.Primitives.Sound;
using Vortex.Primitives.Sound.Providers;

namespace Vortex.Dashboard.API;

/// <summary>
/// Registers the independent admin dashboard API: authentication/session services, the asset store,
/// the read JSON API, the operations layer and the self-hosted HTTP front controller. It consumes
/// Observability (metrics, audit sinks, health/incidents) and Database but owns no observability
/// pipeline of its own — the audit and error-grouping writers live in <c>ObservabilityModule</c> so
/// they run regardless of whether the dashboard is enabled.
/// <para>
/// <b>How this project is laid out.</b> Every subject owns a <c>*Reads</c> class, a
/// <c>*Operations</c> class, or both, grouped into the five families <c>Catalogue</c>,
/// <c>Progression</c>, <c>Hotel</c>, <c>Platform</c> and <c>Safety</c> that <c>Admin</c> also uses.
/// A subject's constructor is the list of what it actually needs, which is the whole point: the two
/// god services this replaced took ten and twenty-nine dependencies and handed all of them to every
/// subject.
/// </para>
/// <para>
/// Folder equals namespace under <c>Api</c> and <c>Operations</c>, as everywhere else in this
/// repository: a file in <c>Operations/Catalogue/</c> declares
/// <c>Vortex.Dashboard.API.Operations.Catalogue</c>. That was impossible while those folders held
/// parts of one partial class, since every part must declare the same namespace.
/// </para>
/// <para>
/// <c>Hosting</c> is the exception, and still is: <see cref="Hosting.DashboardEndpoints"/> remains
/// one partial class across all five family folders, so they all declare
/// <c>Vortex.Dashboard.API.Hosting</c>. That is the tolerated case — thin routing with no
/// dependencies of its own — not a subject hiding its size.
/// </para>
/// <para>
/// Writes all go through <see cref="OperationRunner"/>, which carries the audit, correlation id and
/// reason every operation must produce. It is injected, never inherited, so a subject cannot bend
/// it. Reads share only <see cref="Api.DashboardReads"/>, which opens a context and nothing else.
/// </para>
/// <para>
/// <b>It is a LOADABLE plugin, not a compiled-in one.</b> It used to be an <c>IHostPluginModule</c>,
/// which is a module the host references and registers at startup — optional in principle, welded to
/// the build in practice. As an <see cref="IVortexPlugin"/> it is a folder with a
/// <c>manifest.json</c>: dropped in, activated, unloaded and reloaded while the hotel runs, which is
/// what stops a change to an admin screen from costing a restart of the hotel.
/// </para>
/// <para>
/// Two consequences follow and neither is optional. <b>Nothing in the host may reference this
/// assembly</b> — <c>ByteLoadingAlc</c> hands any assembly the host already holds to the default
/// context, so a project reference would silently make it un-unloadable. And <b>nothing in the host
/// may hold one of its objects</b>, for the same reason at the instance level: what the hotel needs
/// from here goes out through <see cref="BindExportsAsync"/>. <c>DashboardUnloadabilityTests</c> is
/// the measurement that says whether either rule has been broken.
/// </para>
/// </summary>
public sealed class DashboardPlugin : IVortexPlugin
{
    public string Key => "turbo-dashboard-api";

    public string Version => "1.0.0";

    public void ConfigureServices(IServiceCollection services, PluginManifest manifest)
    {
        BorrowFromHost(services);

        // Its own rather than the host's. A plugin container starts empty, and this is a guard
        // object with no shared state — there is nothing to gain by reaching across for it.
        services.TryAddSingleton<RequiredServiceGuard>();

        // NOT registered as IAccountSessionRevoker any more. It used to be, so a password change
        // reached dashboard sessions — but a host that holds one of this assembly's objects pins the
        // whole AssemblyLoadContext and the plugin could never be unloaded. It goes out through
        // BindExportsAsync instead, where the host reads it through an indirection it owns.
        services.TryAddSingleton<DashboardSessionStore>();
        services.TryAddSingleton<DashboardAuthService>();
        services.TryAddSingleton<DashboardAssetStore>();
        services.TryAddSingleton<DashboardAssetUrls>();
        // Singleton because it caches a 38 MB parsed furnidata; a per-request instance would parse it
        // again on every keystroke of a search.
        services.TryAddSingleton<GamedataDocumentStore>();
        services.TryAddSingleton<FiguredataIndex>();
        services.TryAddSingleton<DashboardAuditEmitter>();
        services.TryAddSingleton<DashboardMonitoringReads>();
        // What every dashboard write goes through. A collaborator, so a subject that becomes its
        // own operations class takes it directly instead of inheriting a mechanism.
        services.TryAddSingleton<OperationRunner>();
        // The catalogue, as its own two classes rather than a slice of two god services. Three
        // dependencies for the reads, two for the writes, both visible in their constructors.
        services.TryAddSingleton<CatalogReads>();
        services.TryAddSingleton<CatalogOperations>();
        services.TryAddSingleton<FeedOperations>();
        // The word filter: the reads take the live filter too, for the dry run.
        services.TryAddSingleton<WordFilterReads>();
        services.TryAddSingleton<WordFilterOperations>();
        // Quest content: the reads need nothing but a context.
        services.TryAddSingleton<QuestContentReads>();
        services.TryAddSingleton<QuestContentOperations>();
        services.TryAddSingleton<ArticleReads>();
        services.TryAddSingleton<ArticleOperations>();
        services.TryAddSingleton<SongReads>();
        services.TryAddSingleton<SongOperations>();
        services.TryAddSingleton<GiftWrappingReads>();
        services.TryAddSingleton<GiftWrappingOperations>();
        services.TryAddSingleton<FishingReads>();
        services.TryAddSingleton<FishingOperations>();
        services.TryAddSingleton<NavigatorReads>();
        services.TryAddSingleton<NavigatorOperations>();
        services.TryAddSingleton<FurnitureReads>();
        services.TryAddSingleton<FurnitureOperations>();
        services.TryAddSingleton<QuestReads>();
        services.TryAddSingleton<QuestOperations>();
        services.TryAddSingleton<PrizePoolReads>();
        services.TryAddSingleton<PrizePoolOperations>();
        services.TryAddSingleton<StaffReads>();
        services.TryAddSingleton<StaffOperations>();
        services.TryAddSingleton<ContentOperations>();
        services.TryAddSingleton<GamedataReads>();
        services.TryAddSingleton<GamedataOperations>();
        services.TryAddSingleton<HotelViewReads>();
        services.TryAddSingleton<HotelViewOperations>();
        services.TryAddSingleton<BackupOperations>();
        services.TryAddSingleton<ConsoleOperations>();
        services.TryAddSingleton<PrivacyOperations>();
        services.TryAddSingleton<ModerationOperations>();
        services.TryAddSingleton<StaffActorAccount>();
        services.TryAddSingleton<RoomOperations>();
        services.TryAddSingleton<CurrencyOperations>();
        services.TryAddSingleton<ConfigReads>();
        services.TryAddSingleton<ConfigOperations>();

        services.TryAddSingleton<BotReads>();
        services.TryAddSingleton<GroupReads>();
        services.TryAddSingleton<GuildReads>();
        services.TryAddSingleton<GuildOperations>();
        services.TryAddSingleton<PetReads>();
        services.TryAddSingleton<SocialReads>();
        services.TryAddSingleton<AuditReads>();
        services.TryAddSingleton<ItemAnomalyReads>();
        services.TryAddSingleton<CfhReads>();
        services.TryAddSingleton<ChatlogReads>();
        services.TryAddSingleton<PlayerRewardReads>();
        services.TryAddSingleton<InventoryReads>();
        services.TryAddSingleton<CatalogPurchaseReads>();
        services.TryAddSingleton<EconomyReads>();
        services.TryAddSingleton<AchievementReads>();
        services.TryAddSingleton<AchievementResolutionReads>();
        services.TryAddSingleton<SignalDirectoryReads>();
        services.TryAddSingleton<DirectoryReads>();
        // Authoring content is the dashboard's job, not the emulator's: the admin services live here
        // and are registered here, and a host that does not load this module has no
        // content-authoring path at all -- which is what being an optional plugin means.
        services.TryAddSingleton<IStaffAdminService, StaffAdminService>();
        services.TryAddSingleton<ICatalogAdminService, CatalogAdminService>();
        services.TryAddSingleton<IFishingAdminService, FishingAdminService>();
        services.TryAddSingleton<IFurnitureAdminService, FurnitureAdminService>();
        services.TryAddSingleton<ISongAdminService, SongAdminService>();
        services.TryAddSingleton<IGiftWrappingAdminService, GiftWrappingAdminService>();
        services.TryAddSingleton<INavigatorAdminService, NavigatorAdminService>();
        services.TryAddSingleton<IContentAdminService, ContentAdminService>();
        services.TryAddSingleton<IPrizePoolAdminService, PrizePoolAdminService>();
        services.TryAddSingleton<IQuestAdminService, QuestAdminService>();
        services.TryAddSingleton<IQuestContentAdminService, QuestContentAdminService>();
        services.TryAddSingleton<IWebArticleAdminService, WebArticleAdminService>();
        services.TryAddSingleton<IFeedAdminService, FeedAdminService>();
        services.TryAddSingleton<IWordFilterAdminService, WordFilterAdminService>();

        // The dashboard runs as a self-contained ASP.NET Core (Kestrel) app. Inside the plugin's own
        // container now rather than the host's, which is what makes stopping it a matter of
        // unloading the plugin: PluginManager starts every IHostedService a plugin registers and
        // stops it again on unload.
        services.AddHostedService<DashboardWebHost>();
    }

    /// <summary>
    /// Everything the dashboard needs from the hotel, declared once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A plugin's container starts empty, so without this nothing here could constructor-inject a
    /// database context, a grain factory or a metric. <c>UseHostService&lt;T&gt;</c> makes the host's
    /// instance injectable without the plugin ever seeing the host's provider, and
    /// <c>PluginManager</c> checks every entry at activation — so a hotel missing one of these
    /// refuses the load instead of failing on whichever request first needed it.
    /// </para>
    /// <para>
    /// <b>This list is the contract between the dashboard and the hotel.</b> It was not written by
    /// hand: it is every constructor parameter of every type registered below that the plugin does
    /// not register itself. Adding a dependency here is a deliberate widening of that contract, and
    /// the compiler will not tell you — the activation check will.
    /// </para>
    /// </remarks>
    private static void BorrowFromHost(IServiceCollection services)
    {
        // Configuration and process. ObservabilityConfig is bound by ObservabilityModule in the
        // host; borrowing the bound options rather than re-binding means one section, read once.
        services.UseHostService<IOptions<ObservabilityConfig>>();
        services.UseHostService<IHostEnvironment>();
        services.UseHostService<IHostApplicationLifetime>();

        // Data and the live hotel.
        services.UseHostService<IDbContextFactory<VortexDbContext>>();
        services.UseHostService<IGrainFactory>();
        services.UseHostService<ISessionGateway>();
        services.UseHostService<IDatabaseBackupService>();

        // Observability: the aggregates the overview reads, and the sinks writes go to.
        services.UseHostService<IMeterFactory>();
        services.UseHostService<IVortexMetrics>();
        services.UseHostService<IVortexContextAccessor>();
        services.UseHostService<IAuditSink>();
        services.UseHostService<ILiveStatsAggregator>();
        services.UseHostService<IIncidentDetectionService>();
        services.UseHostService<IInfrastructureHealthService>();
        services.UseHostService<RoomPerformanceAggregator>();
        services.UseHostService<ClientPerformanceMetrics>();
        services.UseHostService<ClubMetrics>();
        services.UseHostService<IForensicsPurgeService>();

        // Accounts and moderation.
        services.UseHostService<IAccountAuthenticator>();
        services.UseHostService<IAccountMfaService>();
        services.UseHostService<IAccountPasswordService>();
        services.UseHostService<IPermissionService>();
        services.UseHostService<ICfhTicketService>();
        // The live word filter: its dry run for the page's test box, and the reload an edit ends on.
        // The host's instance, never a second one — a copy here would reload a list nobody filters with.
        services.UseHostService<IWordFilter>();

        // The catalogues and vocabularies the editors write against, plus the reloader that makes a
        // write visible to the running hotel.
        services.UseHostService<IReferenceDataReloader>();
        services.UseHostService<ICatalogSnapshotProvider<NormalCatalog>>();
        services.UseHostService<IFurnitureDefinitionProvider>();
        services.UseHostService<INavigatorProvider>();
        services.UseHostService<ISongProvider>();
        services.UseHostService<IGiftWrappingProvider>();
        services.UseHostService<ICurrencyTypeProvider>();

        // The console page, which drives the emulator rather than read it.
        services.UseHostService<ServerConsoleFeed>();
        services.UseHostService<IConsoleCommandDispatcher>();
    }

    /// <summary>
    /// Publishes the one thing the hotel needs back from the dashboard.
    /// </summary>
    /// <remarks>
    /// A password change has to sign the account out of the dashboard too, and the dashboard keeps
    /// its own session store. Registering that store in the host's container would work and would
    /// also make this plugin permanently unloadable — the host would hold one of its objects
    /// forever. An export is a slot the HOST owns; this fills it, and every reload refills it.
    /// </remarks>
    public Task BindExportsAsync(IExportBinder binder, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(binder);
        ArgumentNullException.ThrowIfNull(services);

        return binder.ExportAsync<IAccountSessionRevoker>(
            IAccountSessionRevoker.ExportKey,
            services.GetRequiredService<DashboardSessionStore>()
        );
    }

    /// <summary>
    /// Nothing to do: <see cref="DashboardWebHost"/> is an <c>IHostedService</c>, and PluginManager
    /// has already started it by the time this runs. It is here because the interface asks for it,
    /// and an empty body says that deliberately rather than by omission.
    /// </summary>
    public Task StartAsync(IServiceProvider services, CancellationToken ct) => Task.CompletedTask;

    /// <summary>Likewise: the rollback stops the hosted service, in reverse order.</summary>
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Nothing of its own to release — every disposable it owns lives in the plugin's service
    /// provider, which PluginManager disposes before it unloads the context.
    /// </summary>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
