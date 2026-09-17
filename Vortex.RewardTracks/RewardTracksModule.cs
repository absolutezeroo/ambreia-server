using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vortex.Primitives.Hosting;
using Vortex.Primitives.Plugins;
using Vortex.Primitives.RewardTracks;
using Vortex.Primitives.Signals;
using Vortex.RewardTracks.Events;
using Vortex.RewardTracks.Rewards;

namespace Vortex.RewardTracks;

/// <summary>
/// What the reward-track module registers: the cached catalog with its action index, the reward
/// granters, the pipeline that drives them, and the admin service.
/// </summary>
/// <remarks>
/// The granters are the extension point. Adding a reward kind is a class implementing
/// <see cref="IRewardGranter"/> and one line here; the pipeline builds its map from whatever is
/// registered and no existing granter is touched.
/// </remarks>
public sealed class RewardTracksModule : IHostPluginModule
{
    public string Key => "vortex-reward-tracks";

    public void ConfigureServices(IServiceCollection services, HostApplicationBuilder builder)
    {
        services.AddSingleton<RewardTrackCatalog>();
        services.AddSingleton<IRewardTrackCatalog>(sp =>
            sp.GetRequiredService<RewardTrackCatalog>()
        );
        services.AddSingleton<IReferenceDataProvider>(sp =>
            sp.GetRequiredService<RewardTrackCatalog>()
        );

        services.AddSingleton<ISignalInterestSource, RewardTrackSignalInterest>();

        services.AddSingleton<IRewardGranter, CurrencyRewardGranter>();
        services.AddSingleton<IRewardGranter, BadgeRewardGranter>();
        services.AddSingleton<IRewardGranter, FurnitureRewardGranter>();
        services.AddSingleton<IRewardGranter, WallItemRewardGranter>();
        services.AddSingleton<IRewardGranter, AvatarEffectRewardGranter>();
        services.AddSingleton<IRewardGranter, HabbiconRewardGranter>();
        services.AddSingleton<IRewardGranter, EntitlementRewardGranter>();

        // Registered unconditionally, like every other kind. It refuses at grant time on a hotel
        // with no chain rather than being absent from the map: a missing granter logs "no granter
        // for reward kind Coin", which reads like a build mistake and would not be one.
        services.AddSingleton<IRewardGranter, CoinRewardGranter>();

        services.AddSingleton<RewardGrantPipeline>();
    }
}
