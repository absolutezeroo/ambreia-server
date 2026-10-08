using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Vortex.Players.Authentication;
using Vortex.Players.Configuration;
using Vortex.Players.Sessions;
using Vortex.Primitives.Authentication;
using Vortex.Primitives.Hosting;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Sessions;
using Vortex.Primitives.Plugins;

namespace Vortex.Players;

public sealed class PlayerModule : IHostPluginModule
{
    public string Key => "turbo-players";

    public void ConfigureServices(IServiceCollection services, HostApplicationBuilder builder)
    {
        // The game's sign-in: the SSO ticket the client presents. Bound here, with its validator,
        // because the ticket rules are the game's; the owner bootstrap (BootstrapOwnerAccountId, read
        // by the permission seeder) shares the section. Outside Development a placeholder
        // IpHashSecret still refuses to start.
        services
            .AddOptions<AuthenticationConfig>()
            .Bind(builder.Configuration.GetSection(AuthenticationConfig.SECTION_NAME))
            .ValidateOnStart();
        services.AddSingleton<
            IValidateOptions<AuthenticationConfig>,
            AuthenticationConfigValidator
        >();
        services.AddSingleton<IAuthenticationService, AuthenticationService>();

        services.Configure<PlayerPresenceConfig>(
            builder.Configuration.GetSection(PlayerPresenceConfig.SECTION_NAME)
        );
        services.Configure<PlayerDirectoryConfig>(
            builder.Configuration.GetSection(PlayerDirectoryConfig.SECTION_NAME)
        );
        services.AddSingleton<IWorldEntry, WorldEntry>();
    }
}
