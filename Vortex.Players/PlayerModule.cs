using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Vortex.Players.Authentication;
using Vortex.Players.Chat;
using Vortex.Players.Chat.Channels;
using Vortex.Players.Chat.Rules;
using Vortex.Players.Configuration;
using Vortex.Players.Sessions;
using Vortex.Primitives.Authentication;
using Vortex.Primitives.Chat;
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
        // because the ticket rules are the game's. Outside Development a placeholder IpHashSecret
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

        services.Configure<PlayerPresenceConfig>(
            builder.Configuration.GetSection(PlayerPresenceConfig.SECTION_NAME)
        );
        services.Configure<PlayerDirectoryConfig>(
            builder.Configuration.GetSection(PlayerDirectoryConfig.SECTION_NAME)
        );
        services.AddSingleton<IWorldEntry, WorldEntry>();

        services.Configure<ChatConfig>(builder.Configuration.GetSection(ChatConfig.SECTION_NAME));

        // The chat (spec F3c §14): every channel, rule and link kind is declared by the module that
        // owns it; the pipeline keeps the last channel declared for a key.
        services.AddSingleton<IChatChannel, ZoneChatChannel>();
        services.AddSingleton<IChatChannel>(new WorldChatChannel("world"));
        services.AddSingleton<IChatChannel>(new WorldChatChannel("trade"));
        services.AddSingleton<IChatChannel>(new WorldChatChannel("recruitment"));
        services.AddSingleton<IChatChannel, WhisperChatChannel>();
        services.AddSingleton<IChatChannel, SystemChatChannel>();

        // Ready but idle until their systems exist: the group module (M4) and the guild one
        // replace them.
        services.AddSingleton<IChatChannel>(new InactiveChatChannel("group", "not_in_group"));
        services.AddSingleton<IChatChannel>(new InactiveChatChannel("guild", "not_in_guild"));

        services.AddSingleton<IChatRule, ChatLengthRule>();
        services.AddSingleton<IChatRule, ChatRateRule>();
        services.AddSingleton<ChatPipeline>();
    }
}
