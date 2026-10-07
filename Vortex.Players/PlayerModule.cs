using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vortex.Players.Configuration;
using Vortex.Players.Providers;
using Vortex.Players.RewardVault;
using Vortex.Primitives.Content;
using Vortex.Primitives.Hosting;
using Vortex.Primitives.Moderation;
using Vortex.Primitives.Pets.Providers;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Providers;
using Vortex.Primitives.Plugins;

namespace Vortex.Players;

public sealed class PlayerModule : IHostPluginModule
{
    public string Key => "turbo-players";

    public void ConfigureServices(IServiceCollection services, HostApplicationBuilder builder)
    {
        services.Configure<MembershipConfig>(
            builder.Configuration.GetSection(MembershipConfig.SECTION_NAME)
        );
        services.Configure<PlayerPresenceConfig>(
            builder.Configuration.GetSection(PlayerPresenceConfig.SECTION_NAME)
        );

        services.AddSingleton<ICurrencyTypeProvider, CurrencyTypeProvider>();
        services.AddSingleton<IReferenceDataProvider>(sp =>
            (IReferenceDataProvider)sp.GetRequiredService<ICurrencyTypeProvider>()
        );
        services.AddSingleton<IPetSkinProvider, PetSkinProvider>();
        services.AddSingleton<IReferenceDataProvider>(sp =>
            (IReferenceDataProvider)sp.GetRequiredService<IPetSkinProvider>()
        );
        services.AddSingleton<IPetLevelProvider, PetLevelProvider>();
        services.AddSingleton<IReferenceDataProvider>(sp =>
            (IReferenceDataProvider)sp.GetRequiredService<IPetLevelProvider>()
        );
        services.AddSingleton<IRewardVaultSettler, CurrencyRewardVaultSettler>();
        services.AddSingleton<RewardVaultSettlerMap>();

        // The admin services moved to the dashboard: authoring content is its job, not the
        // emulator's. A host without that module has no authoring path here, deliberately.
    }
}
