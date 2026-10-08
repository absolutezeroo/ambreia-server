using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vortex.PacketHandlers.Configuration;
using Vortex.PacketHandlers.Rpg;
using Vortex.Primitives.Plugins;

namespace Vortex.PacketHandlers;

public sealed class PacketHandlersModule : IHostPluginModule
{
    public string Key => "turbo-packet-handlers";

    // Handlers and behaviors are discovered by scanning; what they share is registered here.
    public void ConfigureServices(IServiceCollection services, HostApplicationBuilder builder)
    {
        services.AddSingleton<RpgIntentLimiter>();
        services.Configure<RpgIntentLimitConfig>(
            builder.Configuration.GetSection(RpgIntentLimitConfig.SECTION_NAME)
        );
    }
}
