using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vortex.Primitives.Plugins;

namespace Vortex.PacketHandlers;

public sealed class PacketHandlersModule : IHostPluginModule
{
    public string Key => "turbo-packet-handlers";

    // Handlers are discovered by scanning and their tunables live in IServerConfigGrain: nothing
    // to bind here.
    public void ConfigureServices(IServiceCollection services, HostApplicationBuilder builder) { }
}
