using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Vortex.Crypto.Configuration;
using Vortex.Crypto.Ethereum;
using Vortex.Primitives.Crypto;

namespace Vortex.Crypto.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVortexCrypto(
        this IServiceCollection services,
        HostApplicationBuilder builder
    )
    {
        // Validated at start: RsaService parses these as hex BigIntegers in its constructor, so a
        // placeholder or malformed key used to surface as a FormatException on the first client
        // handshake rather than as a configuration error.
        services
            .AddOptions<CryptoConfig>()
            .Bind(builder.Configuration.GetSection(CryptoConfig.SECTION_NAME))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<CryptoConfig>, CryptoConfigValidator>();

        services.AddSingleton<IRsaService, RsaService>();
        services.AddSingleton<IDiffieService, DiffieService>();

        // Singleton so the master seed is parsed once and the "no seed configured" warning is said
        // once, at startup, rather than on every visit to the Collectors Guild.
        services.AddSingleton<IHotelWalletService, HotelWalletService>();

        // Stateless, but a singleton like the rest: it exists so callers that must not reference
        // this assembly can still verify a wallet's signature.
        services.AddSingleton<IWalletProof, WalletProof>();

        return services;
    }
}
