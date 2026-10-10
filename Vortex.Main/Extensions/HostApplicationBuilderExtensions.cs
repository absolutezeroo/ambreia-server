using System;
using System.Data.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Orleans.Configuration;
using Orleans.Hosting;
using Vortex.Database.Configuration;
using Vortex.Main.Configuration;
using Vortex.Primitives.Orleans;

namespace Vortex.Main.Extensions;

public static class HostApplicationBuilderExtensions
{
    public static HostApplicationBuilder AddOrleans(this HostApplicationBuilder builder)
    {
        // Bound for validation as well as read directly: the silo endpoint must be resolved here,
        // before UseOrleans, but a bad advertised IP or a silo/gateway port collision should be a
        // named startup error rather than an opaque clustering failure later.
        builder
            .Services.AddOptions<OrleansHostConfig>()
            .Bind(builder.Configuration.GetSection(OrleansHostConfig.SECTION_NAME))
            .ValidateOnStart();

        builder.Services.AddSingleton<
            IValidateOptions<OrleansHostConfig>,
            OrleansHostConfigValidator
        >();

        OrleansHostConfig hostConfig =
            builder
                .Configuration.GetSection(OrleansHostConfig.SECTION_NAME)
                .Get<OrleansHostConfig>()
            ?? new OrleansHostConfig();

        bool usesAdoNet =
            hostConfig.ClusteringProvider == "adonet"
            || hostConfig.GrainStorageProvider == "adonet";

        bool unclustered =
            hostConfig.ClusteringProvider == "localhost"
            && hostConfig.GrainStorageProvider == "memory";

        if (!builder.Environment.IsDevelopment() && unclustered)
        {
            if (!hostConfig.AllowUnclusteredOutsideDevelopment)
            {
                throw new InvalidOperationException(
                    "Refusing to start: Orleans would run with single-node localhost clustering and "
                        + "in-memory grain storage/streams outside Development. State does not survive "
                        + "a restart and this cannot scale beyond one silo. Configure a persistent "
                        + $"clustering/storage provider via '{OrleansHostConfig.SECTION_NAME}:"
                        + $"{nameof(OrleansHostConfig.ClusteringProvider)}'/"
                        + $"'{nameof(OrleansHostConfig.GrainStorageProvider)}' (= \"adonet\"), or set "
                        + $"'{OrleansHostConfig.SECTION_NAME}:{nameof(OrleansHostConfig.AllowUnclusteredOutsideDevelopment)}' "
                        + "to true to explicitly accept this for a single-node deployment."
                );
            }

            System.Console.Error.WriteLine(
                "WARNING: Orleans is running with single-node localhost clustering and in-memory "
                    + "grain storage/streams outside Development. State does not survive a restart "
                    + "and this cannot scale beyond one silo. Configure a persistent clustering/"
                    + "storage provider before relying on this in production."
            );
        }

        if (usesAdoNet)
        {
            // MySqlConnector isn't registered as a DbProviderFactory by default (unlike the EF Core
            // path, which references it directly) - Orleans's ADO.NET providers resolve their driver
            // by invariant name through DbProviderFactories.
            DbProviderFactories.RegisterFactory(
                hostConfig.Invariant,
                MySqlConnectorFactory.Instance
            );
        }

        string databaseConnectionString =
            builder
                .Configuration.GetSection(DatabaseConfig.SECTION_NAME)
                .Get<DatabaseConfig>()
                ?.ConnectionString
            ?? string.Empty;

        builder.UseOrleans(
            (System.Action<ISiloBuilder>)(
                silo =>
                {
                    silo.Configure<GrainCollectionOptions>(options =>
                    {
                        options.CollectionAge = hostConfig.GrainCollectionAge;
                    });
                    if (hostConfig.ClusteringProvider == "adonet")
                    {
                        silo.UseAdoNetClustering(options =>
                        {
                            options.Invariant = hostConfig.Invariant;
                            options.ConnectionString = databaseConnectionString;
                        });
                    }
                    else
                    {
                        // The parameterless overload defaults to gateway port 30000 — the port
                        // appsettings.json gives the game TCP listener — and derives the primary
                        // silo endpoint from its own siloPort default. Both must be the configured
                        // values or development clustering points at a silo that isn't there.
                        silo.UseLocalhostClustering(
                            siloPort: hostConfig.SiloPort,
                            gatewayPort: hostConfig.GatewayPort
                        );
                    }

                    // Last writer wins on EndpointOptions, and UseLocalhostClustering configures it
                    // too (forcing loopback and its own ports). Applying the bound configuration
                    // after the clustering provider keeps it authoritative for both branches.
                    //
                    // Neither port carries authentication or TLS, so the silo listens on every
                    // interface only when another silo has to reach it (adonet clustering). A single
                    // in-process silo listens on its advertised (loopback) address and nothing else.
                    silo.ConfigureEndpoints(
                        hostConfig.AdvertisedIp,
                        siloPort: hostConfig.SiloPort,
                        gatewayPort: hostConfig.GatewayPort,
                        listenOnAnyHostAddress: hostConfig.ClusteringProvider == "adonet"
                    );

                    // PLAYER_STORE/ROOM_STORE were never wired to a [PersistentState] grain — every
                    // grain in this codebase persists through EF Core instead, so only PubSubStore
                    // (required by the stream providers below) is actually used.
                    if (hostConfig.GrainStorageProvider == "adonet")
                    {
                        silo.AddAdoNetGrainStorage(
                            OrleansStorageNames.PUB_SUB_STORE,
                            options =>
                            {
                                options.Invariant = hostConfig.Invariant;
                                options.ConnectionString = databaseConnectionString;
                            }
                        );
                    }
                    else
                    {
                        silo.AddMemoryGrainStorage(OrleansStorageNames.PUB_SUB_STORE);
                    }

                    silo.AddMemoryStreams(OrleansStreamProviders.DEFAULT_STREAM_PROVIDER)
                        .AddMemoryStreams(
                            OrleansStreamProviders.ROOM_STREAM_PROVIDER,
                            streams =>
                                streams.ConfigurePullingAgent(ob =>
                                    ob.Configure(options =>
                                    {
                                        // Memory streams are pull-based. Room packets are published
                                        // far apart (an avatar update every 500ms), so the agent is
                                        // always idle when one arrives and every packet paid a
                                        // random slice of the default 100ms poll -- straight into
                                        // the walk cadence the client renders.
                                        options.GetQueueMsgsTimerPeriod = TimeSpan.FromMilliseconds(
                                            10
                                        );
                                    })
                                )
                        );
                }
            )
        );

        return builder;
    }
}
