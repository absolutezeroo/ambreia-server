using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Vortex.Plugins.Configuration;
using Vortex.Primitives.Plugins;

namespace Vortex.Plugins.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVortexPlugins(
        this IServiceCollection services,
        HostApplicationBuilder builder
    )
    {
        IConfigurationSection pluginSection = builder.Configuration.GetSection(
            PluginConfig.SECTION_NAME
        );

        services.AddOptions<PluginConfig>().Bind(pluginSection).ValidateOnStart();

        services.AddSingleton<IValidateOptions<PluginConfig>, PluginConfigValidator>();

        services.AddSingleton<PluginManager>();

        // So the hotel itself can read what a plugin publishes. Without this, exports were a
        // plugin-to-plugin mechanism only, and anything the host wanted from a plugin it had to hold
        // directly — which is exactly what pins a collectible AssemblyLoadContext and stops that
        // plugin from ever being unloaded. See PluginManager.Catalog.
        services.AddSingleton(sp => sp.GetRequiredService<PluginManager>().Catalog);

        services.AddHostedService<PluginBootstrapper>();

        // Read through the config class, never by key name. The section carries no
        // HotReloadEnabled key -- appsettings.json has no Vortex:Plugin section at all -- and
        // GetValue<bool> answers false for a key that is absent instead of falling back to the
        // option's own default. PluginConfig.HotReloadEnabled says true, and hot reload was off
        // in development anyway; binding the section gives the default its say.
        PluginConfig pluginConfig = pluginSection.Get<PluginConfig>() ?? new PluginConfig();

        if (builder.Environment.IsDevelopment() && pluginConfig.HotReloadEnabled)
        {
            services.AddHostedService<PluginHotReloadService>();
        }

        return services;
    }

    public static IServiceCollection AddHostPlugin<TModule>(
        this IServiceCollection services,
        HostApplicationBuilder builder
    )
        where TModule : class, IHostPluginModule, new()
    {
        TModule module = new TModule();

        module.ConfigureServices(services, builder);

        services.AddSingleton<IHostPluginModule>(module);

        return services;
    }
}
