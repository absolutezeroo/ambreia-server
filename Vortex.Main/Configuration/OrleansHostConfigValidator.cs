using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Vortex.Database.Configuration;

namespace Vortex.Main.Configuration;

/// <summary>
/// Validates the Orleans silo endpoint configuration at startup. A bad advertised IP or a port
/// collision here surfaces as an opaque clustering failure well after the host has started
/// announcing itself.
/// </summary>
public sealed class OrleansHostConfigValidator(
    IOptions<DatabaseConfig> databaseConfig,
    IConfiguration configuration
) : IValidateOptions<OrleansHostConfig>
{
    /// <summary>SuperSocket's configuration root, holding one section per game listener server.</summary>
    private const string SERVER_OPTIONS_SECTION = "serverOptions";

    private static readonly string[] KnownClusteringProviders = ["localhost", "adonet"];
    private static readonly string[] KnownGrainStorageProviders = ["memory", "adonet"];

    public ValidateOptionsResult Validate(string? name, OrleansHostConfig options)
    {
        List<string> failures = [];

        if (Array.IndexOf(KnownClusteringProviders, options.ClusteringProvider) < 0)
        {
            failures.Add(
                $"'{OrleansHostConfig.SECTION_NAME}:{nameof(OrleansHostConfig.ClusteringProvider)}' "
                    + $"must be one of: {string.Join(", ", KnownClusteringProviders)} "
                    + $"(got '{options.ClusteringProvider}')."
            );
        }

        if (Array.IndexOf(KnownGrainStorageProviders, options.GrainStorageProvider) < 0)
        {
            failures.Add(
                $"'{OrleansHostConfig.SECTION_NAME}:{nameof(OrleansHostConfig.GrainStorageProvider)}' "
                    + $"must be one of: {string.Join(", ", KnownGrainStorageProviders)} "
                    + $"(got '{options.GrainStorageProvider}')."
            );
        }

        if (
            (options.ClusteringProvider == "adonet" || options.GrainStorageProvider == "adonet")
            && string.IsNullOrWhiteSpace(options.Invariant)
        )
        {
            failures.Add(
                $"'{OrleansHostConfig.SECTION_NAME}:{nameof(OrleansHostConfig.Invariant)}' must be "
                    + "set when clustering or grain storage uses the 'adonet' provider."
            );
        }

        if (
            (options.ClusteringProvider == "adonet" || options.GrainStorageProvider == "adonet")
            && string.IsNullOrWhiteSpace(databaseConfig.Value.ConnectionString)
        )
        {
            failures.Add(
                $"'{OrleansHostConfig.SECTION_NAME}' selects the 'adonet' provider, but "
                    + $"'{DatabaseConfig.SECTION_NAME}:{nameof(DatabaseConfig.ConnectionString)}' is "
                    + "not set. Orleans reuses that connection string rather than a separate one."
            );
        }

        if (!IPAddress.TryParse(options.AdvertisedIp, out IPAddress? advertised))
        {
            failures.Add(
                $"'{OrleansHostConfig.SECTION_NAME}:{nameof(OrleansHostConfig.AdvertisedIp)}' must be "
                    + $"a literal IP address (got '{options.AdvertisedIp}'). Orleans advertises this "
                    + "address to other silos and to gateway clients; host names are not resolved."
            );
        }
        else if (options.ClusteringProvider == "localhost" && !IPAddress.IsLoopback(advertised))
        {
            // Localhost clustering pins the primary silo to 127.0.0.1, and the silo now listens on
            // the advertised address alone: any other address is a silo that cannot find itself.
            failures.Add(
                $"'{OrleansHostConfig.SECTION_NAME}:{nameof(OrleansHostConfig.AdvertisedIp)}' must be "
                    + $"a loopback address with the 'localhost' clustering provider (got "
                    + $"'{options.AdvertisedIp}'). A single in-process silo listens on loopback only; "
                    + "advertise another address with the 'adonet' provider."
            );
        }

        if (options.SiloPort is < 1 or > 65535)
        {
            failures.Add(
                $"'{OrleansHostConfig.SECTION_NAME}:{nameof(OrleansHostConfig.SiloPort)}' must be "
                    + $"between 1 and 65535 (got {options.SiloPort})."
            );
        }

        if (options.GatewayPort is < 1 or > 65535)
        {
            failures.Add(
                $"'{OrleansHostConfig.SECTION_NAME}:{nameof(OrleansHostConfig.GatewayPort)}' must be "
                    + $"between 1 and 65535 (got {options.GatewayPort})."
            );
        }

        // Orleans's own floor is CollectionQuantum (one minute by default); below it the silo throws
        // during startup with a message that never mentions this setting.
        if (options.GrainCollectionAge < TimeSpan.FromMinutes(1))
        {
            failures.Add(
                $"'{OrleansHostConfig.SECTION_NAME}:{nameof(OrleansHostConfig.GrainCollectionAge)}' "
                    + $"must be at least one minute (got {options.GrainCollectionAge}); Orleans "
                    + "cannot collect grains more often than its collection quantum."
            );
        }

        if (options.SiloPort == options.GatewayPort)
        {
            failures.Add(
                $"'{OrleansHostConfig.SECTION_NAME}:{nameof(OrleansHostConfig.SiloPort)}' and "
                    + $"'{OrleansHostConfig.SECTION_NAME}:{nameof(OrleansHostConfig.GatewayPort)}' "
                    + $"are both {options.SiloPort}; the silo and gateway need separate ports."
            );
        }

        // The silo and the game listeners bind independently, so a shared port is not a startup
        // error — it is whichever one started first winning, and the other failing somewhere else.
        foreach ((string serverName, int port) in GameListenerPorts())
        {
            if (port == options.SiloPort)
            {
                failures.Add(
                    $"'{OrleansHostConfig.SECTION_NAME}:{nameof(OrleansHostConfig.SiloPort)}' is "
                        + $"{port}, which is also a '{SERVER_OPTIONS_SECTION}:{serverName}' listener "
                        + "port; the silo and the game listener cannot share a port."
                );
            }

            if (port == options.GatewayPort)
            {
                failures.Add(
                    $"'{OrleansHostConfig.SECTION_NAME}:{nameof(OrleansHostConfig.GatewayPort)}' is "
                        + $"{port}, which is also a '{SERVER_OPTIONS_SECTION}:{serverName}' listener "
                        + "port; the Orleans gateway and the game listener cannot share a port."
                );
            }
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    private IEnumerable<(string ServerName, int Port)> GameListenerPorts()
    {
        foreach (
            IConfigurationSection server in configuration
                .GetSection(SERVER_OPTIONS_SECTION)
                .GetChildren()
        )
        {
            foreach (IConfigurationSection listener in server.GetSection("listeners").GetChildren())
            {
                if (int.TryParse(listener["port"], out int port))
                {
                    yield return (server.Key, port);
                }
            }
        }
    }
}
