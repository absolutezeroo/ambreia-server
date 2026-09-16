using System;
using System.Text.Json;
using System.Threading;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vortex.Chain.Configuration;
using Xunit;

namespace Vortex.Chain.Tests;

/// <summary>
/// The glue between the hotel and its node: how a node's answers are read, and what happens when
/// there is no node at all.
///
/// <para>
/// The off states get most of the attention because they are the default and because the failure
/// they guard against is silent. A hotel with no chain must construct, log, and refuse — not throw
/// at startup, and not appear to work while pointing at nothing.
/// </para>
/// </summary>
public sealed class ChainServiceTests
{
    private const string COIN = "0x5FbDB2315678afecb367f032d93F642f64180aa3";
    private const string COLLECTIBLE = "0xe7f1725E7734CE288F8367e1Bb143E90bb3F0512";
    private const string TREASURY_KEY =
        "0xac0974bec39a17e36ba4a6b4d238ff944bacb478cbed5efcae784d7bf4f2ff80";

    [Fact]
    public void A_hotel_with_no_chain_is_disabled_and_not_broken()
    {
        using ChainService service = Build(new ChainConfig());

        service.Enabled.Should().BeFalse();
        service.TreasuryAddress.Should().BeNull();
    }

    /// <summary>
    /// Every call has to refuse rather than throw a <see cref="NullReferenceException" /> from
    /// somewhere inside, and the message has to say what to check.
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task A_disabled_service_refuses_every_call()
    {
        using ChainService service = Build(new ChainConfig());

        Func<System.Threading.Tasks.Task> read = () =>
            service.GetCoinBalanceAsync(COIN, CancellationToken.None);
        Func<System.Threading.Tasks.Task> mint = () =>
            service.MintCoinAsync(COIN, 1, CancellationToken.None);

        (await read.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*Enabled*");
        await mint.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// An RPC URL set and the rest missing is the shape of a half-finished configuration. It stays
    /// off — the alternative is a hotel that starts, looks enabled, and fails on the first mint.
    /// </summary>
    [Theory]
    [InlineData(null, COLLECTIBLE, TREASURY_KEY)]
    [InlineData(COIN, null, TREASURY_KEY)]
    [InlineData(COIN, COLLECTIBLE, null)]
    // A key that is 31 bytes rather than 32. Long enough to look right, and not a private key.
    [InlineData(
        COIN,
        COLLECTIBLE,
        "0xac0974bec39a17e36ba4a6b4d238ff944bacb478cbed5efcae784d7bf4f2"
    )]
    [InlineData(COIN, "not an address", TREASURY_KEY)]
    public void A_half_configured_chain_stays_off(string? coin, string? collectible, string? key)
    {
        using ChainService service = Build(
            new ChainConfig
            {
                RpcUrl = "http://127.0.0.1:8545",
                CoinAddress = coin,
                CollectibleAddress = collectible,
                TreasuryPrivateKey = key,
            }
        );

        service.Enabled.Should().BeFalse();
    }

    /// <summary>
    /// A complete configuration comes up enabled and knows which account it mints from -- derived
    /// from the key, never configured separately, so the two cannot disagree.
    /// </summary>
    [Fact]
    public void A_configured_chain_derives_its_own_treasury_address()
    {
        using ChainService service = Build(
            new ChainConfig
            {
                RpcUrl = "http://127.0.0.1:8545",
                CoinAddress = COIN,
                CollectibleAddress = COLLECTIBLE,
                TreasuryPrivateKey = TREASURY_KEY,
            }
        );

        service.Enabled.Should().BeTrue();
        service.TreasuryAddress.Should().Be("0xf39Fd6e51aad88F6F4ce6aB8827279cffFb92266");
    }

    /// <summary>
    /// A hex QUANTITY is minimal: <c>0x0</c>, <c>0x2a</c>, an odd number of digits. Parsing it as a
    /// fixed-width value, or with <c>Convert.FromHexString</c>, throws on the majority of real
    /// answers -- and the nonce is one of these.
    /// </summary>
    [Theory]
    [InlineData("0x0", 0UL)]
    [InlineData("0x", 0UL)]
    [InlineData("0x1", 1UL)]
    [InlineData("0x2a", 42UL)]
    [InlineData("0x7a69", 31337UL)]
    [InlineData("0xde0b6b3a7640000", 1_000_000_000_000_000_000UL)]
    public void ParseQuantity_reads_a_minimal_hex_number(string answer, ulong expected)
    {
        JsonRpcClient.ParseQuantity(Json(answer)).Should().Be(expected);
    }

    /// <summary>
    /// Hex DATA is the other convention: whole bytes, and an empty answer is <c>0x</c>. That empty
    /// answer is what a call to an address holding no code returns, so it has to come back as an
    /// empty array rather than throw.
    /// </summary>
    [Theory]
    [InlineData("0x", 0)]
    [InlineData("0xdeadbeef", 4)]
    [InlineData("0x0000000000000000000000000000000000000000000000000000000000000020", 32)]
    public void ParseData_reads_hex_bytes(string answer, int expectedLength)
    {
        JsonRpcClient.ParseData(Json(answer)).Should().HaveCount(expectedLength);
    }

    private static ChainService Build(ChainConfig config) =>
        new(Options.Create(config), NullLogger<ChainService>.Instance);

    private static JsonElement Json(string value) =>
        JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(value));
}
