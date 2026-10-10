using System.Collections.Generic;
using System.Net;
using FluentAssertions;
using Vortex.Networking.Session;
using Xunit;

namespace Vortex.Hosting.Tests.Networking;

/// <summary>
/// Behind the reverse proxy the project recommends, the transport address is the proxy's for
/// everyone: the per-address cap and the audit would see one client. The client's address is taken
/// from what the proxy says — and only from a proxy the operator listed, because anyone can write
/// a PROXY header or an X-Forwarded-For.
/// </summary>
public sealed class ClientAddressTests
{
    private static readonly IPAddress Proxy = IPAddress.Parse("10.0.0.2");
    private static readonly IPAddress Client = IPAddress.Parse("203.0.113.7");
    private static readonly IPAddress Stranger = IPAddress.Parse("198.51.100.9");
    private static readonly IReadOnlySet<IPAddress> NoProxy = new HashSet<IPAddress>();
    private static readonly IReadOnlySet<IPAddress> Trusted = new HashSet<IPAddress> { Proxy };

    [Fact]
    public void WithoutATrustedProxy_TheTransportAddressIsTheClient()
    {
        ClientAddress
            .Resolve(Proxy, Client, "203.0.113.7", NoProxy)
            .Should()
            .Be("10.0.0.2", "a forwarded address from an unknown sender is a forgery");
    }

    [Fact]
    public void FromAStranger_AForwardedAddressIsIgnored()
    {
        ClientAddress.Resolve(Stranger, Client, "203.0.113.7", Trusted).Should().Be("198.51.100.9");
    }

    [Fact]
    public void FromATrustedProxy_TheProxyProtocolSourceWins()
    {
        ClientAddress
            .Resolve(Proxy, Client, "192.0.2.1", Trusted)
            .Should()
            .Be("203.0.113.7", "PROXY protocol is the transport's own word; a header is text");
    }

    [Fact]
    public void FromATrustedProxy_WithoutProxyProtocol_TheFirstForwardedHopIsTheClient()
    {
        ClientAddress
            .Resolve(Proxy, null, "203.0.113.7, 10.0.0.1", Trusted)
            .Should()
            .Be("203.0.113.7");
    }

    [Fact]
    public void FromATrustedProxy_SayingNothing_TheProxyIsTheClient()
    {
        ClientAddress.Resolve(Proxy, null, null, Trusted).Should().Be("10.0.0.2");
    }

    [Fact]
    public void AForwardedHeaderThatIsNotAnAddress_IsIgnored()
    {
        ClientAddress.Resolve(Proxy, null, "unknown", Trusted).Should().Be("10.0.0.2");
    }

    [Fact]
    public void NoTransportAddress_IsNoAddress()
    {
        ClientAddress.Resolve(null, Client, "203.0.113.7", Trusted).Should().BeNull();
    }

    [Fact]
    public void TrustedProxies_AreParsedFromTheConfiguration_AndBadEntriesAreDropped()
    {
        ClientAddress
            .ParseTrusted(["10.0.0.2", "not-an-address", " 10.0.0.3 "])
            .Should()
            .BeEquivalentTo([Proxy, IPAddress.Parse("10.0.0.3")]);
    }
}
