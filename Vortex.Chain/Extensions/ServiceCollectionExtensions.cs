using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vortex.Chain.Configuration;
using Vortex.Primitives.Chain;

namespace Vortex.Chain.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVortexChain(
        this IServiceCollection services,
        HostApplicationBuilder builder
    )
    {
        // No ValidateOnStart, unlike the crypto options next door. Every value here is optional and
        // an empty RpcUrl is the supported default, so there is nothing a validator could refuse
        // that would not amount to refusing to run without a chain. ChainService checks what it was
        // given, says so at startup, and answers Enabled = false.
        services
            .AddOptions<ChainConfig>()
            .Bind(builder.Configuration.GetSection(ChainConfig.SECTION_NAME));

        // Singleton: it holds one HttpClient for the life of the process and a lock that serialises
        // the treasury's nonce. Both of those are wrong per-scope — a second instance would be a
        // second lock, which is no lock at all.
        services.AddSingleton<ChainService>();
        services.AddSingleton<IChainService>(provider =>
            provider.GetRequiredService<ChainService>()
        );

        // How the GAME pays coin, as opposed to how an operator does. Stateless, but a singleton
        // like everything else here so it is built once against the one ChainService above.
        services.AddSingleton<IChainRewardService, ChainRewardService>();

        // Builds it during startup rather than on the first request. A singleton built lazily takes
        // everything its constructor has to say about the configuration — including a throw — to
        // whoever happens to touch it first, which is a stranger's browser and not the operator's
        // log. See ChainStartupProbe.
        services.AddHostedService<ChainStartupProbe>();

        return services;
    }
}
