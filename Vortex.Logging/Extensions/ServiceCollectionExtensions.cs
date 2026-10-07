using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vortex.Logging.Factories;

namespace Vortex.Logging.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVortexLogging(
        this IServiceCollection services,
        HostApplicationBuilder builder
    )
    {
        services.Configure<VortexConsoleFormatterOptions>(
            builder.Configuration.GetSection(VortexConsoleFormatterOptions.SECTION_NAME)
        );

        builder.Logging.ClearProviders();
        builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
        builder.Logging.AddVortexConsoleLogger();

        return services;
    }

    public static IServiceCollection ConfigurePrefixedLogging(
        this IServiceCollection services,
        IServiceProvider host,
        string prefix
    )
    {
        services.AddSingleton<IPrefixedLoggerFactory>(sp => new PrefixedLoggerFactory(
            host.GetRequiredService<ILoggerFactory>(),
            prefix
        ));

        services.Replace(ServiceDescriptor.Singleton(typeof(ILogger<>), typeof(PrefixedLogger<>)));

        return services;
    }
}
