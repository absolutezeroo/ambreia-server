using System.Collections.Generic;
using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Vortex.Main.Configuration;
using Vortex.Main.Extensions;
using Xunit;

namespace Vortex.Hosting.Tests;

/// <summary>
/// <c>Vortex:Orleans:SiloPort</c>/<c>GatewayPort</c> are resolved and handed to
/// <c>ConfigureEndpoints</c>, but <c>UseLocalhostClustering()</c> — the default clustering provider —
/// configures <see cref="EndpointOptions"/> a second time from its own parameter defaults
/// (11111 / 30000). Registered last, it wins, so the configured gateway port was silently discarded
/// and the gateway landed on 30000 — the port <c>appsettings.json</c> gives the game TCP listener.
/// These assert the bound configuration actually reaches the silo.
/// </summary>
public sealed class OrleansEndpointTests
{
    [Fact]
    public void LocalhostClustering_HonoursTheConfiguredGatewayPort()
    {
        EndpointOptions endpoints = ResolveEndpoints(clusteringProvider: "localhost");

        endpoints.GatewayPort.Should().Be(3000);
    }

    [Fact]
    public void LocalhostClustering_HonoursTheConfiguredSiloPort()
    {
        EndpointOptions endpoints = ResolveEndpoints(
            clusteringProvider: "localhost",
            siloPort: 11311
        );

        endpoints.SiloPort.Should().Be(11311);
    }

    /// <summary>
    /// The gateway must never resolve onto the game listener's port — that is a bind race between
    /// the silo and the socket the client connects to, and which one wins is start-order dependent.
    /// </summary>
    [Fact]
    public void TheGatewayDoesNotLandOnTheDefaultGameListenerPort()
    {
        EndpointOptions endpoints = ResolveEndpoints(clusteringProvider: "localhost");

        endpoints.GatewayPort.Should().NotBe(30000);
    }

    /// <summary>
    /// The adonet branch never called <c>UseLocalhostClustering</c>, so it was already correct —
    /// pinned here so the two branches cannot drift apart again.
    /// </summary>
    [Fact]
    public void AdoNetClustering_HonoursTheConfiguredGatewayPort()
    {
        EndpointOptions endpoints = ResolveEndpoints(clusteringProvider: "adonet");

        endpoints.GatewayPort.Should().Be(3000);
    }

    /// <summary>
    /// One silo in one process: nothing outside it has any business reaching 11111 or 3000, and
    /// neither port carries authentication or TLS. Localhost clustering listens on the advertised
    /// (loopback) address, not on every interface.
    /// </summary>
    [Fact]
    public void LocalhostClustering_ListensOnLoopbackOnly()
    {
        EndpointOptions endpoints = ResolveEndpoints(clusteringProvider: "localhost");

        (endpoints.SiloListeningEndpoint?.Address ?? endpoints.AdvertisedIPAddress)
            .Should()
            .Be(IPAddress.Loopback);
        (endpoints.GatewayListeningEndpoint?.Address ?? endpoints.AdvertisedIPAddress)
            .Should()
            .Be(IPAddress.Loopback);
    }

    /// <summary>A second silo has to reach this one, so a clustered deployment keeps the wide bind.</summary>
    [Fact]
    public void AdoNetClustering_ListensOnEveryInterface()
    {
        EndpointOptions endpoints = ResolveEndpoints(clusteringProvider: "adonet");

        endpoints.SiloListeningEndpoint!.Address.Should().Be(IPAddress.Any);
        endpoints.GatewayListeningEndpoint!.Address.Should().Be(IPAddress.Any);
    }

    private static EndpointOptions ResolveEndpoints(
        string clusteringProvider,
        int siloPort = 11111,
        int gatewayPort = 3000
    )
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(
            new HostApplicationBuilderSettings { EnvironmentName = Environments.Development }
        );

        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                [$"{OrleansHostConfig.SECTION_NAME}:SiloPort"] = siloPort.ToString(),
                [$"{OrleansHostConfig.SECTION_NAME}:GatewayPort"] = gatewayPort.ToString(),
                [$"{OrleansHostConfig.SECTION_NAME}:ClusteringProvider"] = clusteringProvider,
                ["Vortex:Database:ConnectionString"] =
                    "server=127.0.0.1;user=root;password=x;database=x",
            }
        );

        builder.AddOrleans();

        using IHost host = builder.Build();

        return host.Services.GetRequiredService<IOptions<EndpointOptions>>().Value;
    }
}
