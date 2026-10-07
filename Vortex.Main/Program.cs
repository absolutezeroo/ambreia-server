using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Crypto.Extensions;
using Vortex.Database.Extensions;
using Vortex.Database.Migrations;
using Vortex.Events.Extensions;
using Vortex.Fishing;
using Vortex.Furniture;
using Vortex.Logging.Extensions;
using Vortex.Main.Configuration;
using Vortex.Main.Console;
using Vortex.Main.Extensions;
using Vortex.Main.Hosting;
using Vortex.Messages.Extensions;
using Vortex.Networking.Extensions;
using Vortex.Observability;
using Vortex.PacketHandlers;
using Vortex.Permissions;
using Vortex.Players;
using Vortex.Plugins.Extensions;
using Vortex.Primitives.Console;
using Vortex.Primitives.Hosting;
using Vortex.Primitives.Orleans;
using Vortex.Progression;
using Vortex.Revisions.Extensions;
using Vortex.Rooms;
using Vortex.Runtime.AssemblyProcessing;
using Vortex.Signals;
using Vortex.WebApi.Authentication;

namespace Vortex.Main;

internal class Program
{
    public static async Task Main(string[] args)
    {
        // The protocol is culture-free, the host it runs on is not. Any `ToString()`/`Parse` that
        // forgets an explicit IFormatProvider would emit "1,5" for a wallpaper id or a furni
        // altitude on a fr-FR machine and read back as garbage on the client. Pinning the default
        // for every thread makes the correct behaviour the fallback rather than the exception.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

        // Held in a `using` for the whole of Main rather than discarded: the factory owns the
        // console provider, and disposing it any earlier would break the logger it created (CA2000).
        using ILoggerFactory bootstrapLoggerFactory = LoggerFactory.Create(builder =>
        {
            builder.ClearProviders();
            builder.AddVortexConsoleLogger();
        });

        ILogger bootstrapLogger = bootstrapLoggerFactory.CreateLogger("Bootstrap");

        System.Console.WriteLine(
            @"
            ██╗   ██╗ ██████╗ ██████╗ ████████╗███████╗██╗  ██╗
            ██║   ██║██╔═══██╗██╔══██╗╚══██╔══╝██╔════╝╚██╗██╔╝
            ██║   ██║██║   ██║██████╔╝   ██║   █████╗   ╚███╔╝
            ╚██╗ ██╔╝██║   ██║██╔══██╗   ██║   ██╔══╝   ██╔██╗
             ╚████╔╝ ╚██████╔╝██║  ██║   ██║   ███████╗██╔╝ ██╗
              ╚═══╝   ╚═════╝ ╚═╝  ╚═╝   ╚═╝   ╚══════╝╚═╝  ╚═╝
            "
        );

        bootstrapLogger.LogInformation(
            "Starting {GetProjectName} {GetProductVersion}",
            GetProjectName(),
            GetProjectVersion()
        );

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        builder.Configuration.AddEnvironmentVariables(prefix: "VORTEX__");

        if (builder.Environment.IsDevelopment())
        {
            bootstrapLogger.LogInformation("=== Configuration Providers ===");
            foreach (
                IConfigurationProvider p in ((IConfigurationRoot)builder.Configuration).Providers
            )
            {
                if (p is JsonConfigurationProvider jp)
                {
                    JsonConfigurationSource src = (JsonConfigurationSource)jp.Source;
                    string? path = src.Path;

                    if (path is not null)
                    {
                        IFileProvider? fileProvider =
                            src.FileProvider ?? builder.Environment.ContentRootFileProvider;
                        IFileInfo? fi = fileProvider?.GetFileInfo(path);
                        string physical = fi?.PhysicalPath ?? "<virtual or unresolved>";

                        bootstrapLogger.LogInformation(
                            "Json: '{Path}' -> {Physical}",
                            path,
                            physical
                        );
                    }
                }
            }
            bootstrapLogger.LogInformation("===============================");
        }

        builder.AddOrleans();

        builder.Services.AddVortexLogging(builder);
        builder.Services.AddVortexNetworking(builder);
        builder.Services.AddVortexPlugins(builder);
        builder.Services.AddVortexDatabaseContext(builder);
        builder.Services.AddVortexEventSystem();
        builder.Services.AddVortexMessageSystem(builder);
        builder.Services.AddVortexCrypto(builder);
        builder.Services.AddVortexRevisions(builder);

        builder.Services.AddHostPlugin<ObservabilityModule>(builder);
        builder.Services.AddHostPlugin<PermissionsModule>(builder);
        builder.Services.AddHostPlugin<WebApiAuthenticationModule>(builder);
        builder.Services.AddHostPlugin<FurnitureModule>(builder);
        builder.Services.AddHostPlugin<PlayerModule>(builder);
        builder.Services.AddHostPlugin<SignalsModule>(builder);
        builder.Services.AddHostPlugin<ProgressionModule>(builder);
        builder.Services.AddHostPlugin<FishingModule>(builder);
        // No dashboard or web API module here any more. Both are LOADABLE plugins, found in their
        // folders by PluginManager rather than compiled into this host — registering either here
        // would mean referencing its assembly, which is exactly what makes a plugin impossible to
        // unload.
        builder.Services.AddHostPlugin<RoomModule>(builder);
        builder.Services.AddHostPlugin<PacketHandlersModule>(builder);

        builder.Services.AddSingleton<AssemblyProcessor>();
        builder.Services.AddSingleton<IConsoleCommandDispatcher, ConsoleCommandDispatcher>();
        builder.Services.AddSingleton<IMaintenanceAnnouncer, MaintenanceAnnouncer>();
        builder
            .Services.AddOptions<ShutdownConfig>()
            .Bind(builder.Configuration.GetSection(ShutdownConfig.SECTION_NAME));

        ShutdownConfig shutdown =
            builder.Configuration.GetSection(ShutdownConfig.SECTION_NAME).Get<ShutdownConfig>()
            ?? new ShutdownConfig();

        if (shutdown.NoticeMinutes > 0)
        {
            // The host cancels the token it hands StoppingAsync once this elapses, and it defaults
            // to 30 seconds — which would cut every notice longer than that off at the knees, from
            // inside, with the orchestrator still patiently waiting. DrainSeconds is the shutdown
            // the notice is holding open: the socket teardown and the grain deactivations that do
            // the writing.
            builder.Services.Configure<HostOptions>(options =>
                options.ShutdownTimeout =
                    TimeSpan.FromMinutes(shutdown.NoticeMinutes)
                    + TimeSpan.FromSeconds(shutdown.DrainSeconds)
            );
        }

        // The runtime half of reference-data loading. VortexEmulator is the startup half and shares
        // nothing with it: their error policies are opposites.
        //
        // The two grain-held caches are composed in here rather than known about by the reloader.
        // They are not IReferenceDataProvider singletons -- they live inside a grain and reload on a
        // grain call -- which is why each had grown its own console command. Naming them here makes
        // `reload` the one word for the whole idea.
        builder.Services.AddSingleton<IReferenceDataReloader>(sp => new ReferenceDataReloader(
            sp.GetServices<IReferenceDataProvider>(),
            sp.GetRequiredService<ILogger<ReferenceDataReloader>>(),
            new Dictionary<string, Func<CancellationToken, Task>>(StringComparer.Ordinal)
            {
                ["FishingDefinitions"] = ct =>
                    sp.GetRequiredService<IGrainFactory>()
                        .GetFishingDefinitionsGrain()
                        .ReloadAsync(ct),
            }
        ));
        builder.Services.AddSingleton<ConsoleCommandService>();

        builder.Services.AddHostedService<VortexEmulator>();

        IHost host = builder.Build();

        IHostApplicationLifetime lifetime =
            host.Services.GetRequiredService<IHostApplicationLifetime>();
        CancellationToken ct = lifetime.ApplicationStopping;

        try
        {
            // Before StartAsync, so the schema is settled before a listener accepts anyone. Opt-in
            // via Vortex:Database:MigrateOnStartup; a failure here lands in the catch below and
            // exits non-zero, which is the right answer -- a host running against a schema its code
            // does not match answers 500 to every login instead.
            await MigrationHelper
                .ApplyStartupMigrationsAsync(host.Services, ct)
                .ConfigureAwait(false);

            await host.StartAsync(ct).ConfigureAwait(false);

            bootstrapLogger.LogInformation(
                "Started {GetProjectName} {GetProductVersion}",
                GetProjectName(),
                GetProjectVersion()
            );

            host.Services.GetService<ConsoleCommandService>()?.Enable();

            await host.WaitForShutdownAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            bootstrapLogger.LogCritical(ex, "Host terminated unexpectedly");

            // Fail loudly to the process supervisor (systemd/k8s/container runtime): without a
            // non-zero exit code a fatal startup/runtime failure looks like a clean shutdown and
            // won't trigger a restart or alert.
            System.Environment.ExitCode = 1;
        }
    }

    private static string GetProjectName()
    {
        return "Vortex Emulator";
    }

    public static Version GetProjectVersion()
    {
        return new Version(
            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0"
        );
    }
}
