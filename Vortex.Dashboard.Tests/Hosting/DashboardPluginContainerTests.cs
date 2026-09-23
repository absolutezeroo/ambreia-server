using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vortex.Dashboard.API;
using Vortex.Dashboard.API.Hosting;
using Vortex.Primitives.Plugins;
using Xunit;

namespace Vortex.Dashboard.Tests.Hosting;

/// <summary>
/// The plugin's container starts empty: everything an endpoint's service needs is either
/// registered by the plugin or borrowed from the host in <c>BorrowFromHost</c>. A dependency on
/// neither compiles, passes every unit test that builds the service by hand, and then fails the
/// whole dashboard at startup — <c>ForwardSingletons</c> resolves every forwarded type eagerly.
/// </summary>
/// <remarks>
/// Found the hard way: <c>WordFilterReads</c> took <c>IWordFilter</c>, which the host registers
/// and the plugin never borrowed, and nothing short of starting the emulator said so.
/// </remarks>
public sealed class DashboardPluginContainerTests
{
    [Fact]
    public async Task EveryForwardedServicesConstructorDependency_IsRegisteredInThePluginContainer()
    {
        ServiceCollection services = [];
        await using DashboardPlugin plugin = new();

        plugin.ConfigureServices(
            services,
            new PluginManifest
            {
                Name = "Vortex Dashboard",
                Key = "turbo-dashboard-api",
                Version = "1.0.0",
                Author = "Vortex",
                AssemblyFile = "Vortex.Dashboard.API.dll",
            }
        );

        HashSet<Type> registered = [.. services.Select(d => d.ServiceType)];

        List<string> missing = [];

        foreach (Type forwarded in DashboardWebHost.ForwardedServiceTypes)
        {
            ServiceDescriptor? descriptor = services.LastOrDefault(d => d.ServiceType == forwarded);

            // A factory or an instance — which is what a borrowed host service is — builds nothing
            // from constructor parameters here, so there is nothing of the plugin's to check.
            if (descriptor is not null && descriptor.ImplementationType is null)
            {
                continue;
            }

            // What the container would build: the implementation behind the service type.
            Type implementation = descriptor?.ImplementationType ?? forwarded;

            ConstructorInfo? constructor = implementation
                .GetConstructors(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                )
                .OrderByDescending(c => c.GetParameters().Length)
                .FirstOrDefault();

            if (constructor is null)
            {
                continue;
            }

            foreach (ParameterInfo parameter in constructor.GetParameters())
            {
                Type type = parameter.ParameterType;

                // Logging is added to every plugin container by the host's plugin manager.
                if (
                    type == typeof(ILoggerFactory)
                    || type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ILogger<>)
                )
                {
                    continue;
                }

                if (!registered.Contains(type))
                {
                    missing.Add($"{implementation.Name}({type.Name} {parameter.Name})");
                }
            }
        }

        // Joined, because a collection assertion prints only the first item and every miss matters.
        string.Join(", ", missing)
            .Should()
            .BeEmpty(
                "every dependency must be registered by the plugin or borrowed in BorrowFromHost"
            );
    }
}
