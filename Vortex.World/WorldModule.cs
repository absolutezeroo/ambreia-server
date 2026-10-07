using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vortex.Primitives.Plugins;

namespace Vortex.World;

/// <summary>Zones built by the map editor: storage, validation, publication.</summary>
public sealed class WorldModule : IHostPluginModule
{
    public string Key => "vortex-world";

    public void ConfigureServices(IServiceCollection services, HostApplicationBuilder builder)
    {
        services.Configure<WorldConfig>(builder.Configuration.GetSection(WorldConfig.SECTION_NAME));
        services.AddSingleton<ZoneValidator>();
    }
}
