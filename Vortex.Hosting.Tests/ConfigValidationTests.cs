using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Vortex.Crypto.Configuration;
using Vortex.Database.Configuration;
using Vortex.Main.Configuration;
using Vortex.Observability.Configuration;
using Vortex.Players.Authentication;
using Vortex.Plugins.Configuration;
using Vortex.Primitives.Authentication;
using Vortex.WebApi.Configuration;
using Xunit;

namespace Vortex.Hosting.Tests;

/// <summary>
/// Sensitive configuration used to be bound and never checked, so the failure surfaced far from the
/// cause: a placeholder RSA key threw a <c>FormatException</c> on the first client handshake, a
/// <c>CHANGE_ME</c> connection string failed every query, an unloadable certificate produced a
/// listener that was advertised as HTTPS but was not. These validators turn all of that into one
/// named startup error.
/// </summary>
public sealed class ConfigValidationTests
{
    // ── Crypto ───────────────────────────────────────────────────────────────

    /// <summary>The real 1024-bit development modulus, used as the "known good" baseline.</summary>
    private const string VALID_MODULUS =
        "E228E8E9C40CFB0D2081B165376E921B844B5E581D88DBEA54CC038DA3CD9B00DC7281C172FCF3DF64D595EAA12BFF08BE7B346E95A920D05FC84B4E079C071BFFBF810D0BD0C994A302DD61200DDEFE28DC59BB3B803B243625FCEDC6665715ADCCF7CD94EA7A4B70AF8F0A7BD1097B50B306B871CBEAD90966CF1A1A4809C3";

    private const string VALID_PRIVATE =
        "25B17C26F60229D7856AF2E633E7C304960C8FB95A4179FC637755ECF0A2448024BDC04AE87F7DFA90CE43A71ADCAA81751488BD18F185780FF6B737ABEF56845A5F89ED33F91B5D5CEF03180501CFF9981230BE66B634D40079FF2DE4FAA6C444834CE7685158691A162B427CED179C04A0F4B4789FD16EAFE7F5850B309B6B";

    [Fact]
    public void AValidCryptoConfig_Passes() => ValidateCrypto(Crypto()).Succeeded.Should().BeTrue();

    [Fact]
    public void TheShippedPlaceholderKeys_AreRefused()
    {
        ValidateOptionsResult result = ValidateCrypto(
            Crypto(
                publicKey: "CHANGE_ME__generate-a-fresh-1024-bit-RSA-keypair-set-via-VORTEX__Vortex__Crypto__PublicKey"
            )
        );

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("placeholder");
    }

    [Fact]
    public void ANonHexadecimalKey_IsRefused()
    {
        // RsaService parses these with new BigInteger(value, 16); a stray character used to throw
        // FormatException on the first connecting client instead of at startup.
        ValidateCrypto(Crypto(publicKey: "ZZZZ")).Failed.Should().BeTrue();
    }

    [Fact]
    public void AnEmptyKey_IsRefused() =>
        ValidateCrypto(Crypto(privateKey: "")).Failed.Should().BeTrue();

    [Fact]
    public void AModulusThatIsTooShort_IsRefused()
    {
        ValidateOptionsResult result = ValidateCrypto(Crypto(publicKey: "FF"));

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("bits");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("4")]
    public void AnInvalidPublicExponent_IsRefused(string exponent) =>
        ValidateCrypto(Crypto(keySize: exponent)).Failed.Should().BeTrue();

    [Theory]
    [InlineData("3")]
    [InlineData("10001")]
    public void TheUsualPublicExponents_Pass(string exponent) =>
        ValidateCrypto(Crypto(keySize: exponent)).Succeeded.Should().BeTrue();

    // ── Database ─────────────────────────────────────────────────────────────

    [Fact]
    public void AChangeMeConnectionString_IsRefused()
    {
        ValidateOptionsResult result = new DatabaseConfigValidator().Validate(
            null,
            new DatabaseConfig
            {
                ConnectionString =
                    "CHANGE_ME__set-via-VORTEX__Vortex__Database__ConnectionString-env-var-or-user-secrets",
            }
        );

        result.Failed.Should().BeTrue();
    }

    [Fact]
    public void TheConnectionString_IsNeverEchoedInTheFailureMessage()
    {
        const string SECRET = "server=db;user=root;password=CHANGE_ME_hunter2;database=vortex";

        ValidateOptionsResult result = new DatabaseConfigValidator().Validate(
            null,
            new DatabaseConfig { ConnectionString = SECRET }
        );

        result.Failed.Should().BeTrue();
        result
            .FailureMessage.Should()
            .NotContain(
                "hunter2",
                "validation failures are logged, and this holds the DB password"
            );
    }

    [Fact]
    public void AnEmptyConnectionString_IsRefused() =>
        new DatabaseConfigValidator()
            .Validate(null, new DatabaseConfig { ConnectionString = "" })
            .Failed.Should()
            .BeTrue();

    [Fact]
    public void ARealConnectionString_Passes() =>
        new DatabaseConfigValidator()
            .Validate(
                null,
                new DatabaseConfig
                {
                    ConnectionString = "server=127.0.0.1;user=root;database=vortex",
                }
            )
            .Succeeded.Should()
            .BeTrue();

    // ── Authentication ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("local-dev-ip-hash-secret")]
    [InlineData("replace-with-a-development-secret")]
    [InlineData("replace-with-a-production-secret")]
    [InlineData("")]
    public void APlaceholderIpHashSecret_IsRefusedOutsideDevelopment(string secret)
    {
        AuthenticationConfigValidator validator = new(Environment(Environments.Production));

        validator
            .Validate(null, new AuthenticationConfig { IpHashSecret = secret })
            .Failed.Should()
            .BeTrue();
    }

    [Fact]
    public void APlaceholderIpHashSecret_IsToleratedInDevelopment()
    {
        AuthenticationConfigValidator validator = new(Environment(Environments.Development));

        validator
            .Validate(
                null,
                new AuthenticationConfig { IpHashSecret = "replace-with-a-development-secret" }
            )
            .Succeeded.Should()
            .BeTrue();
    }

    /// <summary>
    /// SEC-15. Each bound is individually optional for a defensible reason — single-use is off so a
    /// CMS can reuse one ticket across reconnects — and turning both off leaves no bound at all:
    /// every use slides the TTL forward, so an observed ticket stays valid for as long as somebody
    /// keeps replaying it. These three cases are the whole rule.
    /// </summary>
    [Fact]
    public void NeitherTicketReplayBound_IsRefusedOutsideDevelopment()
    {
        AuthenticationConfigValidator validator = new(Environment(Environments.Production));

        validator
            .Validate(
                null,
                new AuthenticationConfig
                {
                    IpHashSecret = "a-real-production-secret",
                    TicketSingleUse = false,
                    TicketAbsoluteLifetimeSeconds = null,
                }
            )
            .Failed.Should()
            .BeTrue();
    }

    [Fact]
    public void AnAbsoluteTicketLifetime_IsEnoughOnItsOwn()
    {
        AuthenticationConfigValidator validator = new(Environment(Environments.Production));

        validator
            .Validate(
                null,
                new AuthenticationConfig
                {
                    IpHashSecret = "a-real-production-secret",
                    TicketSingleUse = false,
                    TicketAbsoluteLifetimeSeconds = 300,
                }
            )
            .Succeeded.Should()
            .BeTrue();
    }

    [Fact]
    public void SingleUseTickets_AreEnoughOnTheirOwn()
    {
        AuthenticationConfigValidator validator = new(Environment(Environments.Production));

        validator
            .Validate(
                null,
                new AuthenticationConfig
                {
                    IpHashSecret = "a-real-production-secret",
                    TicketSingleUse = true,
                    TicketAbsoluteLifetimeSeconds = null,
                }
            )
            .Succeeded.Should()
            .BeTrue();
    }

    [Fact]
    public void ANegativeTicketTtl_IsRefused()
    {
        AuthenticationConfigValidator validator = new(Environment(Environments.Development));

        validator
            .Validate(null, new AuthenticationConfig { TicketTtlSeconds = -1 })
            .Failed.Should()
            .BeTrue();
    }

    // ── WebApi listener + certificate ────────────────────────────────────────

    [Fact]
    public void ADisabledWebApi_IsNeverValidated()
    {
        // Nothing binds a socket, so an otherwise-insecure configuration cannot bite.
        WebApiConfig config = new()
        {
            Enabled = false,
            Host = "0.0.0.0",
            HttpsEnabled = false,
        };

        ValidateWebApi(config, Environments.Production).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void AnEnabledWebApiOnARemoteAddressWithoutHttps_IsRefused()
    {
        WebApiConfig config = new()
        {
            Enabled = true,
            Host = "0.0.0.0",
            HttpsEnabled = false,
        };

        ValidateWebApi(config, Environments.Production).Failed.Should().BeTrue();
    }

    [Fact]
    public void AnEnabledWebApiOnLocalhost_Passes_InDevelopment()
    {
        WebApiConfig config = new() { Enabled = true, Host = "localhost" };

        ValidateWebApi(config, Environments.Development).Succeeded.Should().BeTrue();
    }

    /// <summary>
    /// Outside Development, a web API without TLS of its own is only reachable through a reverse
    /// proxy. The login, registration and ticket limiters partition by the connection's address,
    /// which is then the proxy's: one bucket for the whole hotel, five logins a minute for everyone.
    /// </summary>
    [Fact]
    public void ALoopbackWebApiOutsideDevelopment_WithoutForwardedHeaders_IsRefused()
    {
        WebApiConfig config = new() { Enabled = true, Host = "localhost" };

        ValidateOptionsResult result = ValidateWebApi(config, Environments.Production);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(WebApiConfig.UseForwardedHeaders));
    }

    [Fact]
    public void ALoopbackWebApiOutsideDevelopment_BehindADeclaredProxy_Passes()
    {
        WebApiConfig config = new()
        {
            Enabled = true,
            Host = "localhost",
            UseForwardedHeaders = true,
            KnownProxies = ["10.0.0.1"],
        };

        ValidateWebApi(config, Environments.Production).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void ARemoteWebApiWithoutHttpsOutsideDevelopment_WithoutForwardedHeaders_IsRefused()
    {
        // The TLS it lacks is a proxy's: the forwarded address is the one the limiters need.
        WebApiConfig config = new()
        {
            Enabled = true,
            Host = "0.0.0.0",
            HttpsEnabled = false,
            AllowInsecureRemoteHttp = true,
        };

        ValidateOptionsResult result = ValidateWebApi(config, Environments.Production);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(WebApiConfig.UseForwardedHeaders));
    }

    [Fact]
    public void HttpsWithoutACertificate_IsRefusedOutsideDevelopment()
    {
        // Outside Development there is no dev certificate to fall back on, so this would come up as
        // plain HTTP while still being advertised as HTTPS.
        WebApiConfig config = new()
        {
            Enabled = true,
            Host = "localhost",
            HttpsEnabled = true,
            CertificatePath = null,
        };

        ValidateWebApi(config, Environments.Production).Failed.Should().BeTrue();
    }

    [Fact]
    public void HttpsWithoutACertificate_IsToleratedInDevelopment()
    {
        WebApiConfig config = new()
        {
            Enabled = true,
            Host = "localhost",
            HttpsEnabled = true,
            CertificatePath = null,
        };

        ValidateWebApi(config, Environments.Development).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void HttpsWithAMissingCertificateFile_IsRefused()
    {
        WebApiConfig config = new()
        {
            Enabled = true,
            Host = "localhost",
            HttpsEnabled = true,
            CertificatePath = Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.pfx"),
        };

        ValidateOptionsResult result = ValidateWebApi(config, Environments.Development);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("does not exist");
    }

    [Fact]
    public void HttpsWithAnUnloadableCertificateFile_IsRefused()
    {
        string path = Path.Combine(Path.GetTempPath(), $"broken-{Guid.NewGuid():N}.pfx");
        File.WriteAllText(path, "this is not a PKCS#12 archive");

        try
        {
            WebApiConfig config = new()
            {
                Enabled = true,
                Host = "localhost",
                HttpsEnabled = true,
                CertificatePath = path,
            };

            ValidateOptionsResult result = ValidateWebApi(config, Environments.Development);

            result.Failed.Should().BeTrue();
            result.FailureMessage.Should().Contain("could not be loaded");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ForwardedHeadersWithoutAKnownProxy_IsRefused()
    {
        // Trusting X-Forwarded-For from an unnamed sender lets any client forge its source IP, which
        // is what the login and registration rate limits partition on.
        WebApiConfig config = new()
        {
            Enabled = true,
            Host = "localhost",
            UseForwardedHeaders = true,
        };

        ValidateWebApi(config, Environments.Production).Failed.Should().BeTrue();
    }

    [Fact]
    public void ForwardedHeadersWithAKnownProxy_Passes()
    {
        WebApiConfig config = new()
        {
            Enabled = true,
            Host = "localhost",
            UseForwardedHeaders = true,
            KnownProxies = ["10.0.0.1"],
        };

        ValidateWebApi(config, Environments.Production).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void ForwardedHeadersWithAKnownNetwork_Passes()
    {
        WebApiConfig config = new()
        {
            Enabled = true,
            Host = "localhost",
            UseForwardedHeaders = true,
            KnownNetworks = ["10.0.0.0/8"],
        };

        ValidateWebApi(config, Environments.Production).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void AnOutOfRangePort_IsRefused()
    {
        WebApiConfig config = new()
        {
            Enabled = true,
            Host = "localhost",
            Port = 0,
        };

        ValidateWebApi(config, Environments.Production).Failed.Should().BeTrue();
    }

    private static OrleansHostConfigValidator OrleansValidator(
        string connectionString = "server=x",
        params int[] gameListenerPorts
    ) =>
        new(
            Options.Create(new DatabaseConfig { ConnectionString = connectionString }),
            GameListenerConfiguration(gameListenerPorts)
        );

    /// <summary>Shapes ports into the SuperSocket layout the validator reads them from.</summary>
    private static IConfiguration GameListenerConfiguration(int[] ports)
    {
        Dictionary<string, string?> values = [];

        for (int i = 0; i < ports.Length; i++)
        {
            values[$"serverOptions:TcpServer:listeners:{i}:port"] = ports[i].ToString();
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void TheDefaultOrleansEndpoint_Passes() =>
        OrleansValidator().Validate(null, new OrleansHostConfig()).Succeeded.Should().BeTrue();

    [Fact]
    public void ANonLiteralAdvertisedIp_IsRefused() =>
        OrleansValidator()
            .Validate(null, new OrleansHostConfig { AdvertisedIp = "silo.internal" })
            .Failed.Should()
            .BeTrue();

    [Fact]
    public void ACollidingSiloAndGatewayPort_IsRefused() =>
        OrleansValidator()
            .Validate(null, new OrleansHostConfig { SiloPort = 11111, GatewayPort = 11111 })
            .Failed.Should()
            .BeTrue();

    [Fact]
    public void AGatewayPortOnTheGameListener_IsRefused()
    {
        // The shipped appsettings.json puts the TCP listener on 30000, which is also Orleans's own
        // default gateway port — so this collision is one careless config edit away.
        ValidateOptionsResult result = OrleansValidator(gameListenerPorts: [30000])
            .Validate(null, new OrleansHostConfig { GatewayPort = 30000 });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("game listener");
    }

    [Fact]
    public void ASiloPortOnTheGameListener_IsRefused() =>
        OrleansValidator(gameListenerPorts: [11111])
            .Validate(null, new OrleansHostConfig { SiloPort = 11111 })
            .Failed.Should()
            .BeTrue();

    [Fact]
    public void TheShippedPortLayout_Passes() =>
        OrleansValidator(gameListenerPorts: [30000, 30001])
            .Validate(null, new OrleansHostConfig { SiloPort = 11111, GatewayPort = 3000 })
            .Succeeded.Should()
            .BeTrue();

    [Fact]
    public void AnUnknownClusteringProvider_IsRefused() =>
        OrleansValidator()
            .Validate(null, new OrleansHostConfig { ClusteringProvider = "zookeeper" })
            .Failed.Should()
            .BeTrue();

    [Fact]
    public void AdoNetClusteringWithoutAConnectionString_IsRefused() =>
        OrleansValidator(connectionString: string.Empty)
            .Validate(null, new OrleansHostConfig { ClusteringProvider = "adonet" })
            .Failed.Should()
            .BeTrue();

    [Fact]
    public void AdoNetClusteringWithAConnectionString_Passes() =>
        OrleansValidator()
            .Validate(null, new OrleansHostConfig { ClusteringProvider = "adonet" })
            .Succeeded.Should()
            .BeTrue();

    [Fact]
    public void AGrainCollectionAgeBelowTheQuantum_IsRefused() =>
        OrleansValidator()
            .Validate(null, new OrleansHostConfig { GrainCollectionAge = TimeSpan.FromSeconds(30) })
            .Failed.Should()
            .BeTrue();

    [Fact]
    public void ALongerGrainCollectionAge_Passes() =>
        OrleansValidator()
            .Validate(null, new OrleansHostConfig { GrainCollectionAge = TimeSpan.FromMinutes(30) })
            .Succeeded.Should()
            .BeTrue();

    // ── Plugins ──────────────────────────────────────────────────────────────

    [Fact]
    public void TheDefaultPluginConfig_Passes() =>
        new PluginConfigValidator().Validate(null, new PluginConfig()).Succeeded.Should().BeTrue();

    [Fact]
    public void ANegativeDebounce_IsRefused() =>
        new PluginConfigValidator()
            .Validate(null, new PluginConfig { DebounceMs = -1 })
            .Failed.Should()
            .BeTrue();

    [Fact]
    public void AnEmptyPluginFolderPath_IsRefused() =>
        new PluginConfigValidator()
            .Validate(null, new PluginConfig { PluginFolderPath = "  " })
            .Failed.Should()
            .BeTrue();

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static CryptoConfig Crypto(
        string keySize = "3",
        string? publicKey = null,
        string? privateKey = null
    ) =>
        new()
        {
            KeySize = keySize,
            PublicKey = publicKey ?? VALID_MODULUS,
            PrivateKey = privateKey ?? VALID_PRIVATE,
            EnableServerToClientEncryption = true,
        };

    private static ValidateOptionsResult ValidateCrypto(CryptoConfig config) =>
        new CryptoConfigValidator().Validate(null, config);

    private static ValidateOptionsResult ValidateWebApi(WebApiConfig config, string environment) =>
        new WebApiConfigValidator(Environment(environment)).Validate(null, config);

    private static IHostEnvironment Environment(string environmentName) =>
        new FakeEnvironment { EnvironmentName = environmentName };

    private sealed class FakeEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "Vortex.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider
        {
            get => new Microsoft.Extensions.FileProviders.NullFileProvider();
            set { }
        }
    }
}
