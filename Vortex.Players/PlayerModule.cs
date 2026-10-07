using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Vortex.Players.Authentication;
using Vortex.Players.Configuration;
using Vortex.Players.Providers;
using Vortex.Players.RewardVault;
using Vortex.Primitives.Authentication;
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
        // The game's sign-in: the SSO ticket the client presents. Bound here, with its validator,
        // because the ticket rules are the game's; the web accounts and the owner bootstrap read the
        // same section (IpHashSecret, BootstrapOwnerEmail). Outside Development a placeholder secret
        // still refuses to start.
        services
            .AddOptions<AuthenticationConfig>()
            .Bind(builder.Configuration.GetSection(AuthenticationConfig.SECTION_NAME))
            .ValidateOnStart();
        services.AddSingleton<
            IValidateOptions<AuthenticationConfig>,
            AuthenticationConfigValidator
        >();
        services.AddSingleton<IAuthenticationService, AuthenticationService>();

        // A password change signs the account out everywhere, and everywhere includes surfaces a
        // plugin owns. This is the host's own stable object; what it delegates to is swapped by
        // whichever plugin publishes a revoker, so the hotel never holds a plugin's type — which is
        // exactly what would make that plugin unloadable for the life of the process. See
        // PluginSessionRevoker. One per plugin that signs people in — the dashboard and the web
        // API — because an export slot holds a single instance.
        services.AddSingleton<IAccountSessionRevoker>(sp => new PluginSessionRevoker(
            sp.GetRequiredService<IPluginCatalog>(),
            IAccountSessionRevoker.ExportKey
        ));
        services.AddSingleton<IAccountSessionRevoker>(sp => new PluginSessionRevoker(
            sp.GetRequiredService<IPluginCatalog>(),
            IAccountSessionRevoker.WebApiExportKey
        ));

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
