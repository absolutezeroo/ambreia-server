using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vortex.Plugins.Configuration;
using Vortex.Plugins.Extensions;
using Xunit;

namespace Vortex.Plugins.Tests;

/// <summary>
/// Whether an option's own default reaches the code that acts on it.
/// </summary>
/// <remarks>
/// Hot reload used to register itself on <c>pluginSection.GetValue&lt;bool&gt;("HotReloadEnabled")</c>,
/// read off the raw configuration section rather than off <see cref="PluginConfig"/>. There is no
/// <c>Vortex:Plugin</c> section in <c>appsettings.json</c> at all, and <c>GetValue</c> answers
/// <c>false</c> for a key that is not there instead of falling back to the property's initialiser —
/// so <c>PluginHotReloadService</c> never started in development even though the option it reads
/// says <c>true</c>. Nothing was wrong to look at: the build was green, the option was there, and
/// the service simply was not in the container.
/// </remarks>
public sealed class PluginHotReloadRegistrationTests
{
    [Fact]
    public void InDevelopmentWithNoSection_TheOptionsDefaultRegistersHotReload()
    {
        HostApplicationBuilder builder = NewBuilder(Environments.Development);

        builder.Services.AddVortexPlugins(builder);

        IsHotReloadRegistered(builder.Services)
            .Should()
            .BeTrue(
                "PluginConfig.HotReloadEnabled defaults to true and nothing overrides it, which is "
                    + "the shipping configuration"
            );
    }

    [Fact]
    public void InDevelopmentWithTheSectionSayingFalse_HotReloadIsNotRegistered()
    {
        HostApplicationBuilder builder = NewBuilder(Environments.Development);

        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                [$"{PluginConfig.SECTION_NAME}:HotReloadEnabled"] = "false",
            }
        );

        builder.Services.AddVortexPlugins(builder);

        IsHotReloadRegistered(builder.Services)
            .Should()
            .BeFalse("a hotel that turns the option off is read, not just the default");
    }

    private static HostApplicationBuilder NewBuilder(string environmentName) =>
        Host.CreateEmptyApplicationBuilder(
            new HostApplicationBuilderSettings { EnvironmentName = environmentName }
        );

    private static bool IsHotReloadRegistered(IServiceCollection services) =>
        services.Any(descriptor => descriptor.ImplementationType == typeof(PluginHotReloadService));
}
