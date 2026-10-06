using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Vortex.Crypto.Configuration;
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

        return services;
    }
}
