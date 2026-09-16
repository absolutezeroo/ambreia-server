using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Crypto.Ethereum;
using Xunit;

namespace Vortex.WebApi.Tests;

/// <summary>
/// Linking a self-custody wallet, through the real pipeline and against real signatures.
/// </summary>
/// <remarks>
/// <para>
/// The endpoint's whole job is to tell a proof from a claim, so the tests sign for real — the test
/// host registers <c>WalletProof</c> itself rather than a fake, because a fake that answers "yes,
/// that address signed it" would assert the plumbing and nothing about the one rule that matters.
/// </para>
/// <para>
/// On a hotel that counts balances, an address accepted without proof is a way to claim somebody
/// else's score, and an address accepted for two accounts is a way to count one balance twice. Both
/// are covered below.
/// </para>
/// </remarks>
public sealed class WalletEndpointTests
{
    /// <summary>Anvil's second genesis account. Any key does; this one is easy to recognise.</summary>
    private static readonly byte[] PlayerKey = Convert.FromHexString(
        "59c6995e998f97a5a0044966f0945389dc9e86dae88c7a8412f4603b6b78690d"
    );

    private const string PlayerAddress = "0x70997970C51812dc3A010C7d01b50e0d17dc79C8";

    [Fact]
    public async Task Wallets_StartWithTheHotelsOwnAndNothingElse()
    {
        await using WebApiTestFactory factory = new();

        JsonElement body = await ReadAsync(factory.CreateAuthenticatedClient());

        // Derived, never stored — so it is there before the player has done anything at all.
        body.GetProperty("stardustAddress").GetString().Should().StartWith("0x");
        body.GetProperty("linkedAddresses").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ASignedChallenge_LinksTheWalletThatSignedIt()
    {
        await using WebApiTestFactory factory = new();
        using HttpClient client = factory.CreateAuthenticatedClient();

        JsonElement linked = await LinkAsync(client, PlayerKey);

        linked
            .GetProperty("linkedAddresses")
            .EnumerateArray()
            .Should()
            .ContainSingle()
            .Which.GetString()
            .Should()
            .Be(PlayerAddress);
    }

    /// <summary>
    /// The refusal the feature exists for. Anybody can type anybody's address; only its holder can
    /// sign for it.
    /// </summary>
    [Fact]
    public async Task AnAddressNobodyProved_IsRefused()
    {
        await using WebApiTestFactory factory = new();
        using HttpClient client = factory.CreateAuthenticatedClient();

        await ChallengeAsync(client);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/user/wallets",
            new { address = PlayerAddress, signature = "0x" + new string('0', 130) }
        );

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadAsync(client)).GetProperty("linkedAddresses").GetArrayLength().Should().Be(0);
    }

    /// <summary>
    /// A signature is only worth what it was made over. Signing the hotel's challenge is what links
    /// a wallet; signing anything else links nothing.
    /// </summary>
    [Fact]
    public async Task ASignatureOverSomethingElse_LinksNothing()
    {
        await using WebApiTestFactory factory = new();
        using HttpClient client = factory.CreateAuthenticatedClient();

        await ChallengeAsync(client);

        string elsewhere =
            "0x"
            + Convert.ToHexString(
                EthereumTransaction.SignPersonalMessage("some other hotel's challenge", PlayerKey)
            );

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/user/wallets",
            new { address = PlayerAddress, signature = elsewhere }
        );

        // Recovery succeeds — it always does — and answers a stranger, so the claimed address does
        // not match it.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// One nonce, one link. A challenge that survived its use would let a captured signature be
    /// presented again.
    /// </summary>
    [Fact]
    public async Task AChallengeIsSpentByTheFirstUse()
    {
        await using WebApiTestFactory factory = new();
        using HttpClient client = factory.CreateAuthenticatedClient();

        string message = await ChallengeAsync(client);
        string signature =
            "0x" + Convert.ToHexString(EthereumTransaction.SignPersonalMessage(message, PlayerKey));

        using HttpResponseMessage first = await client.PostAsJsonAsync(
            "/api/user/wallets",
            new { address = PlayerAddress, signature }
        );
        using HttpResponseMessage second = await client.PostAsJsonAsync(
            "/api/user/wallets",
            new { address = PlayerAddress, signature }
        );

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unlinking_RemovesItAndLetsItBeLinkedAgain()
    {
        await using WebApiTestFactory factory = new();
        using HttpClient client = factory.CreateAuthenticatedClient();

        await LinkAsync(client, PlayerKey);

        using HttpResponseMessage removed = await client.DeleteAsync(
            $"/api/user/wallets/{PlayerAddress}"
        );

        removed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync(client)).GetProperty("linkedAddresses").GetArrayLength().Should().Be(0);

        // The row is deleted rather than tombstoned precisely so this works: a unique address with
        // a soft delete would refuse the same wallet forever after.
        JsonElement again = await LinkAsync(client, PlayerKey);

        again.GetProperty("linkedAddresses").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Wallets_NeedASession()
    {
        await using WebApiTestFactory factory = new();

        using HttpResponseMessage response = await factory.Client.GetAsync("/api/user/wallets");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<string> ChallengeAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/user/wallets/challenge",
            new { }
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // The message is taken from the answer and never rebuilt here: the browser signs the exact
        // bytes the server will verify, and a test that composed its own would stop noticing the
        // day the two drifted.
        return body.GetProperty("message").GetString()!;
    }

    private static async Task<JsonElement> LinkAsync(HttpClient client, byte[] key)
    {
        string message = await ChallengeAsync(client);
        string signature =
            "0x" + Convert.ToHexString(EthereumTransaction.SignPersonalMessage(message, key));

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/user/wallets",
            new { signature }
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> ReadAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync("/api/user/wallets");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
