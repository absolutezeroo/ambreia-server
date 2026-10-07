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
using Vortex.Primitives.Observability;
using Vortex.Primitives.Plugins;

namespace Vortex.Observability;

/// <summary>
/// Registers the observability module: the trace-context accessor (correlation id + Orleans
/// propagation + logging scope + activity), the metrics façade, the durable audit and error
/// pipelines (sinks -> bounded channel -> background writer), the retention sweep and the
/// grain-call filter.
/// </summary>
public sealed class ObservabilityModule : IHostPluginModule
{
    public string Key => "vortex-observability";

    public void ConfigureServices(IServiceCollection services, HostApplicationBuilder builder)
    {
        services
            .AddOptions<ObservabilityConfig>()
            .Bind(builder.Configuration.GetSection(ObservabilityConfig.SECTION_NAME));

        services.AddMetrics();

        services.TryAddSingleton<IVortexContextAccessor, VortexContextAccessor>();
        services.TryAddSingleton<IVortexMetrics, VortexMetrics>();
        services.AddHostedService<ForensicsRetentionService>();

        // Hosted only so the container instantiates it — its gauges observe the session gateway on
        // scrape and it has no work of its own to start.
        services.AddHostedService<ConnectionMetrics>();

        // Error-grouping pipeline: bounded channel -> single background writer (no DB on the hot path).
        services.AddSingleton<ErrorGroupingChannel>();
        services.TryAddSingleton<IErrorGroupingSink, ChannelErrorGroupingSink>();
        services.AddHostedService<ErrorGroupingWriterService>();

        // Durable audit pipeline: one bounded channel -> single background writer (no DB on the hot path).
        services.AddSingleton<AuditChannel>();
        services.TryAddSingleton<IAuditSink, ChannelAuditSink>();
        services.TryAddSingleton<IItemForensics, ChannelItemForensics>();
        services.AddHostedService<AuditWriterService>();

        // Orleans resolves all registered IIncomingGrainCallFilter instances from the container.
        services.AddSingleton<IIncomingGrainCallFilter, ObservabilityGrainCallFilter>();
    }
}
