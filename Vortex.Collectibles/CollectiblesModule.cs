using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vortex.Collectibles.Vault;
using Vortex.Primitives.Plugins;

namespace Vortex.Collectibles;

/// <summary>
/// What the collectibles module registers. The vault settlers are the extension point: adding a
/// reward type the Earnings window can pay is a class implementing <see cref="IVaultRewardSettler"/>
/// and one line here.
/// </summary>
public sealed class CollectiblesModule : IHostPluginModule
{
    public string Key => "vortex-collectibles";

    public void ConfigureServices(IServiceCollection services, HostApplicationBuilder builder)
    {
        services.AddSingleton<IVaultRewardSettler, CurrencyVaultRewardSettler>();
        services.AddSingleton<VaultRewardSettlerMap>();
    }
}
