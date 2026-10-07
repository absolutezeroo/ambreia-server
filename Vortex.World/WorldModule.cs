using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Plugins;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Editing;

namespace Vortex.World;

/// <summary>Zones built by the map editor: storage, validation, publication.</summary>
public sealed class WorldModule : IHostPluginModule
{
    public string Key => "vortex-world";

    public void ConfigureServices(IServiceCollection services, HostApplicationBuilder builder)
    {
        services.Configure<WorldConfig>(builder.Configuration.GetSection(WorldConfig.SECTION_NAME));
        services.AddSingleton<ZoneValidator>();
        services.AddSingleton(sp => new ZoneRoomReloader(roomId =>
            sp.GetRequiredService<IGrainFactory>().GetRoomGrain(roomId).ReloadZoneAsync()
        ));
        services.AddSingleton<IZoneEditorService, ZoneEditorService>();
        services.AddSingleton<IZoneRoomSource, ZoneRoomSource>();
    }
}
