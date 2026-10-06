using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vortex.Primitives.Plugins;

namespace Vortex.Collectibles;

/// <summary>What the collectibles module registers (nothing yet).</summary>
public sealed class CollectiblesModule : IHostPluginModule
{
    public string Key => "vortex-collectibles";

    public void ConfigureServices(IServiceCollection services, HostApplicationBuilder builder) { }
}
