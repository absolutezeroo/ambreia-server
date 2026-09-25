using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vortex.Events.Registry;
using Vortex.Primitives.Plugins;
using Vortex.WebApi.Hosting;
using Xunit;

namespace Vortex.WebApi.Tests;

/// <summary>
/// The plugin's container starts empty: everything the web API needs is either registered by
/// <see cref="WebApiPlugin"/> or borrowed from the hotel in its <c>BorrowFromHost</c>. A dependency
/// on neither compiles and passes every test that goes through <see cref="WebApiTestFactory"/> —
/// which builds its own container — and then fails the whole web API at startup.
/// </summary>
/// <remarks>
/// The host is faked by answering every borrowed type with a stand-in, so what is measured is the
/// plugin's own wiring, not the hotel's: a borrow the real host lacks is PluginManager's activation
/// check to catch.
/// </remarks>
public sealed class WebApiPluginContainerTests
{
    [Fact]
    public async Task EveryRegisteredService_CanBeBuilt()
    {
        await using ServiceProvider provider = await BuildPluginContainerAsync();

        // ValidateOnBuild already walked every constructor; resolving the hosted service also runs
        // the options binding and the config validator through the borrowed configuration.
        provider.GetServices<IHostedService>().OfType<WebApiWebHost>().Should().ContainSingle();
    }

    [Fact]
    public async Task EveryServiceTheKestrelAppIsHanded_ResolvesFromThePluginContainer()
    {
        await using ServiceProvider provider = await BuildPluginContainerAsync();

        WebApiWebHost host = provider
            .GetServices<IHostedService>()
            .OfType<WebApiWebHost>()
            .Single();

        MethodInfo forward = typeof(WebApiWebHost).GetMethod(
            "ForwardSingletons",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;

        // Throws, naming the type, on the first one neither registered nor borrowed.
        Action act = () => forward.Invoke(host, [new ServiceCollection()]);

        act.Should().NotThrow();
    }

    [Fact]
    public async Task EveryEventHandler_CanBeBuiltFromThePluginContainer()
    {
        await using ServiceProvider provider = await BuildPluginContainerAsync();

        // PluginManager resolves these from the plugin's container, not the host's.
        Type[] handlers =
        [
            .. typeof(WebApiPlugin)
                .Assembly.GetTypes()
                .Where(type =>
                    type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }
                    && type.GetInterfaces()
                        .Any(i =>
                            i.IsGenericType
                            && i.GetGenericTypeDefinition() == typeof(IEventHandler<>)
                        )
                ),
        ];

        handlers.Should().NotBeEmpty();

        List<string> failed = [];

        foreach (Type handler in handlers)
        {
            try
            {
                ActivatorUtilities.CreateInstance(provider, handler);
            }
            catch (InvalidOperationException ex)
            {
                failed.Add($"{handler.Name}: {ex.Message}");
            }
        }

        string.Join(Environment.NewLine, failed).Should().BeEmpty();
    }

    private static async Task<ServiceProvider> BuildPluginContainerAsync()
    {
        ServiceCollection services = [];

        // What PluginManager puts in every plugin container before the plugin's own registrations.
        services.AddLogging();
        services.AddSingleton<IHostServices>(new FakeHost());

        await using WebApiPlugin plugin = new();

        plugin.ConfigureServices(
            services,
            new PluginManifest
            {
                Name = "Vortex Web API",
                Key = "turbo-webapi",
                Version = "1.0.0",
                Author = "Vortex",
                AssemblyFile = "Vortex.WebApi.dll",
            }
        );

        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
        );
    }

    /// <summary>
    /// Answers every borrowed type. Configuration is real, because the plugin binds its options from
    /// it; everything else is an inert proxy that is never called.
    /// </summary>
    private sealed class FakeHost : IHostServices
    {
        private readonly IConfiguration _configuration = new ConfigurationBuilder().Build();

        public T GetRequiredService<T>()
            where T : notnull =>
            typeof(T) == typeof(IConfiguration)
                ? (T)_configuration
                : (T)DispatchProxy.Create(typeof(T), typeof(InertProxy));
    }

    public class InertProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.ReturnType is { IsValueType: true } type && type != typeof(void)
                ? Activator.CreateInstance(type)
                : null;
    }
}
