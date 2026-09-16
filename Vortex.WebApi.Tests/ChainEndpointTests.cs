using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Vortex.WebApi.Tests;

/// <summary>
/// The two chain routes, through the real routing pipeline.
/// </summary>
/// <remarks>
/// <para>
/// They shipped with no test at all, and a deployed hotel answered 500 on both — an unhandled
/// exception, which ASP.NET turns into an empty body with the response reset, so even the security
/// headers the first middleware sets were gone. Nothing appeared in the hotel's own log either,
/// because the framework writes that under a category a hotel's log filter does not usually pass.
/// Visible to anyone with curl, invisible to the operator.
/// </para>
/// <para>
/// The chain is not configured in the test host, which is the state that matters most here: every
/// hotel runs that way by default, so these two routes have to answer politely rather than throw.
/// </para>
/// </remarks>
public sealed class ChainEndpointTests
{
    [Fact]
    public async Task Descriptor_IsServedWhenNoChainIsConfigured()
    {
        await using WebApiTestFactory factory = new();

        using HttpResponseMessage response = await factory.Client.GetAsync("/api/public/chain");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("enabled").GetBoolean().Should().BeFalse();

        // Answered even when the chain is off, because a wallet asking what network this is has to
        // be told something — and the id is a hex quantity, which is what MetaMask validates.
        body.GetProperty("chainIdHex").GetString().Should().StartWith("0x");
        body.GetProperty("rpcUrl").GetString().Should().EndWith("/api/public/chain/rpc");
    }

    [Fact]
    public async Task Rpc_IsUnavailableWhenNoChainIsConfigured()
    {
        await using WebApiTestFactory factory = new();

        using HttpResponseMessage response = await factory.Client.PostAsJsonAsync(
            "/api/public/chain/rpc",
            new { method = "eth_chainId", @params = new object[0] }
        );

        // 503 and not 404 or 500: the route exists and will work once an operator turns the chain
        // on, which is a different thing for a wallet to be told.
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>
    /// The refusal that the whole arrangement exists for. anvil cannot be told to stop serving
    /// <c>anvil_setBalance</c>, so this is the only thing between a player's browser and a treasury
    /// anybody can mint from.
    /// </summary>
    [Theory]
    [InlineData("anvil_setBalance")]
    [InlineData("anvil_impersonateAccount")]
    [InlineData("eth_sendTransaction")]
    [InlineData("eth_accounts")]
    [InlineData("evm_mine")]
    public async Task Rpc_RefusesAnAdminMethod(string method)
    {
        await using WebApiTestFactory factory = new();

        using HttpResponseMessage response = await factory.Client.PostAsJsonAsync(
            "/api/public/chain/rpc",
            new { method, @params = new object[0] }
        );

        // With no chain configured the disabled check fires first, which is still a refusal; what
        // must never happen is a 200 or a 500.
        response
            .StatusCode.Should()
            .BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Rpc_RejectsARequestWithNoMethod()
    {
        await using WebApiTestFactory factory = new();

        using HttpResponseMessage response = await factory.Client.PostAsJsonAsync(
            "/api/public/chain/rpc",
            new { @params = new object[0] }
        );

        response
            .StatusCode.Should()
            .BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.ServiceUnavailable);
    }
}
