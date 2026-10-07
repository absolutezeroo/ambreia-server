using FluentAssertions;
using Vortex.Primitives.Hosting;
using Xunit;

namespace Vortex.Hosting.Tests;

/// <summary>
/// The HTTP surface carries credentials — the web API takes registrations and logins. Binding it
/// to a non-local address without TLS puts all of that on the wire in cleartext, and it used to be a one-line
/// config change with nothing to stop it.
/// </summary>
public sealed class ListenerSecurityTests
{
    private const string OPTION = "Vortex:WebApi:AllowInsecureRemoteHttp";

    [Theory]
    [InlineData("localhost")]
    [InlineData("LOCALHOST")]
    [InlineData("127.0.0.1")]
    [InlineData("127.10.20.30")]
    [InlineData("::1")]
    [InlineData("[::1]")]
    public void LocalAddresses_AreLocal(string host) =>
        ListenerSecurity.IsLocalHost(host).Should().BeTrue();

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("[::]")]
    [InlineData("*")]
    [InlineData("+")]
    [InlineData("192.168.1.10")]
    [InlineData("203.0.113.5")]
    [InlineData("hotel.example.com")]
    [InlineData("")]
    [InlineData(null)]
    public void WildcardAndRemoteAddresses_AreNotLocal(string? host) =>
        ListenerSecurity.IsLocalHost(host).Should().BeFalse();

    [Fact]
    public void LocalhostOverHttp_IsAllowed()
    {
        ListenerSecurityResult result = Validate("localhost", httpsEnabled: false, allow: false);

        result.IsAllowed.Should().BeTrue();
        result.Message.Should().BeNull();
    }

    [Fact]
    public void WildcardBindOverHttp_IsRefused()
    {
        // 0.0.0.0 accepts traffic on every interface, which is the remote-exposure case even though
        // it does not look like a remote address.
        Validate("0.0.0.0", httpsEnabled: false, allow: false).IsAllowed.Should().BeFalse();
    }

    [Fact]
    public void ARemoteAddressOverHttp_IsRefused()
    {
        Validate("203.0.113.5", httpsEnabled: false, allow: false).IsAllowed.Should().BeFalse();
    }

    [Fact]
    public void ARemoteAddressOverHttps_IsAllowed()
    {
        Validate("203.0.113.5", httpsEnabled: true, allow: false).IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void ARemoteAddressOverHttp_IsAllowedWhenExplicitlyAccepted()
    {
        ListenerSecurityResult result = Validate("203.0.113.5", httpsEnabled: false, allow: true);

        result.IsAllowed.Should().BeTrue();
        result
            .Message.Should()
            .NotBeNull("accepting cleartext remote exposure must still be warned about");
    }

    [Fact]
    public void TheRefusalMessage_TellsTheOperatorEverythingNeededToAct()
    {
        string message = Validate("203.0.113.5", httpsEnabled: false, allow: false).Message!;

        message.Should().Contain("203.0.113.5", "the operator must see which address was refused");
        message.Should().Contain("8080", "…and on which port");
        message.Should().Contain("Vortex web API", "…and which service");
        message.Should().Contain("cleartext", "…what the actual risk is");
        message.Should().Contain(OPTION, "…and the exact option that accepts it");
    }

    private static ListenerSecurityResult Validate(string host, bool httpsEnabled, bool allow) =>
        ListenerSecurity.ValidateListener(
            "Vortex web API",
            host,
            8080,
            httpsEnabled,
            allow,
            OPTION
        );
}
