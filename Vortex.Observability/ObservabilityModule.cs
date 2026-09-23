using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Observability.Audit;
using Vortex.Observability.Configuration;
using Vortex.Observability.Context;
using Vortex.Observability.ErrorTracking;
using Vortex.Observability.Metrics;
using Vortex.Observability.Runtime;
using Vortex.Observability.WordFilter;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Plugins;
using Vortex.Primitives.Signals;
using WordFilterService = Vortex.Observability.WordFilter.WordFilter;

namespace Vortex.Observability;

/// <summary>
/// Registers the observability module: the trace-context accessor (correlation id + Orleans
/// propagation + logging scope + activity), the metrics façade, the durable audit pipeline
/// (event-handler sinks -> bounded channel -> background writer), the grain-call filter and the
/// native admin dashboard (self-disabled unless configured with a token).
/// </summary>
public sealed class ObservabilityModule : IHostPluginModule
{
    public string Key => "vortex-observability";

    public void ConfigureServices(IServiceCollection services, HostApplicationBuilder builder)
    {
        // DashboardApiModule binds the same section; both register the validator through
        // TryAddEnumerable so it runs exactly once regardless of module order.
        services
            .AddOptions<ObservabilityConfig>()
            .Bind(builder.Configuration.GetSection(ObservabilityConfig.SECTION_NAME))
            .ValidateOnStart();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IValidateOptions<ObservabilityConfig>,
                ObservabilityConfigValidator
            >()
        );

        services.AddMetrics();

        services.TryAddSingleton<IVortexContextAccessor, VortexContextAccessor>();
        services.TryAddSingleton<ILiveStatsAggregator, LiveStatsAggregator>();
        services.TryAddSingleton<IVortexMetrics, VortexMetrics>();
        services.TryAddSingleton<ClubMetrics>();
        services.TryAddSingleton<ISignalMetrics, SignalMetrics>();
        services.AddHostedService<ClubMetricsRefreshService>();
        services.AddHostedService<ForensicsRetentionService>();
        services.AddSingleton<IForensicsPurgeService, ForensicsPurgeService>();

        // Hosted only so the container instantiates it — its gauges observe the session gateway on
        // scrape and it has no work of its own to start.
        services.AddHostedService<ConnectionMetrics>();

        // Reads the room instruments back off the meter for the dashboard. Registered as a singleton
        // *and* as the hosted service that starts it, so the dashboard resolves the same instance
        // that is actually listening rather than a second, silent one.
        services.TryAddSingleton<RoomPerformanceAggregator>();
        services.AddHostedService(sp => sp.GetRequiredService<RoomPerformanceAggregator>());

        services.TryAddSingleton<ClientPerformanceMetrics>();
        services.TryAddSingleton<IPerformanceLogSink>(sp =>
            sp.GetRequiredService<ClientPerformanceMetrics>()
        );
        services.TryAddSingleton<IInfrastructureHealthService, InfrastructureHealthService>();
        services.TryAddSingleton<IIncidentDetectionService, IncidentDetectionService>();

        // Error-grouping pipeline: bounded channel -> single background writer (no DB on the hot path).
        services.AddSingleton<ErrorGroupingChannel>();
        services.TryAddSingleton<IErrorGroupingSink, ChannelErrorGroupingSink>();
        services.AddHostedService<ErrorGroupingWriterService>();

        // Durable audit pipeline: one bounded channel -> single background writer (no DB on the hot path).
        services.AddSingleton<AuditChannel>();
        services.TryAddSingleton<IAuditSink, ChannelAuditSink>();
        services.TryAddSingleton<IEconomyLedger, ChannelEconomyLedger>();
        services.TryAddSingleton<IItemForensics, ChannelItemForensics>();
        services.AddHostedService<AuditWriterService>();

        // The hotel-wide word filter. Its journal rides the channel above; the loader performs the
        // first reload once the silo is up.
        services.TryAddSingleton<IWordFilter, WordFilterService>();
        services.AddHostedService<WordFilterLoader>();

        // Orleans resolves all registered IIncomingGrainCallFilter instances from the container.
        services.AddSingleton<IIncomingGrainCallFilter, ObservabilityGrainCallFilter>();
    }
}
