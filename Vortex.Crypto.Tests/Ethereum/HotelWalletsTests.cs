using System;
using System.Collections.Generic;
using FluentAssertions;
using Vortex.Crypto.Ethereum;
using Xunit;

namespace Vortex.Crypto.Tests.Ethereum;

/// <summary>
/// Safety net for <see cref="HotelWallets" />, the custodial wallet every player is given.
///
/// <para>
/// The two halves are tested differently on purpose, because only one of them CAN be cross-checked.
/// Key-to-address is the arithmetic every wallet in existence performs, so the three expectations
/// below came out of <c>ethers</c> and a failure there means this hotel would hand a player an
/// address nothing can find. Seed-to-key is this hotel's own scheme with no reference to compare
/// against, so what is pinned is the set of properties the scheme has to have: same input, same
/// wallet, forever; different player or different seed, different wallet; and a short seed refused
/// rather than quietly stretched.
/// </para>
///
/// <para>
/// <see cref="DeriveAddress_matches_an_independent_implementation_of_the_scheme" /> does pin four
/// derived addresses to literals, which was worth a second thought: it freezes
/// <c>DERIVATION_LABEL</c> into the test suite as well as into the code. It is pinned anyway,
/// because it is the ONLY thing that catches a silent change to the derivation, and a silent change
/// to the derivation strands every wallet in every hotel running this build. Read the failure the
/// right way round -- it does not mean an expected string is stale, it means the wallets moved.
/// </para>
/// </summary>
public sealed class HotelWalletsTests
{
    private static readonly byte[] SEED = Convert.FromHexString(
        "6f1a2b3c4d5e6f708192a3b4c5d6e7f8091a2b3c4d5e6f708192a3b4c5d6e7f8"
    );

    private static readonly byte[] OTHER_SEED = Convert.FromHexString(
        "0000000000000000000000000000000000000000000000000000000000000001"
    );

    [Theory]
    // Anvil's genesis accounts 0 and 1: the pairs a developer will be looking at in the node's own
    // startup banner, so a mismatch is visible without any tooling.
    [InlineData(
        "ac0974bec39a17e36ba4a6b4d238ff944bacb478cbed5efcae784d7bf4f2ff80",
        "0xf39Fd6e51aad88F6F4ce6aB8827279cffFb92266"
    )]
    [InlineData(
        "59c6995e998f97a5a0044966f0945389dc9e86dae88c7a8412f4603b6b78690d",
        "0x70997970C51812dc3A010C7d01b50e0d17dc79C8"
    )]
    [InlineData(
        "4c0883a69102937d6231471b5dbb6204fe5129617082792ae468d01a3f362318",
        "0x2c7536E3605D9C16a7a3D7b1898e529396a65c23"
    )]
    public void AddressOfPrivateKey_matches_the_reference_implementation(
        string privateKeyHex,
        string expected
    )
    {
        HotelWallets
            .AddressOfPrivateKey(Convert.FromHexString(privateKeyHex))
            .Should()
            .Be(expected);
    }

    [Theory]
    // Zero and n are both outside [1, n-1]; n is the interesting one, because it is the boundary the
    // rejection sampling in DerivePrivateKey exists to stay under.
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("fffffffffffffffffffffffffffffffebaaedce6af48a03bbfd25e8cd0364141")]
    [InlineData("ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")]
    public void AddressOfPrivateKey_refuses_a_scalar_off_the_curve(string privateKeyHex)
    {
        Action derive = () =>
            HotelWallets.AddressOfPrivateKey(Convert.FromHexString(privateKeyHex));

        derive.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// The seed-to-key half, against a second implementation of the scheme written from
    /// <see cref="HotelWallets" />' documentation in Node -- <c>crypto.createHmac</c> for the
    /// derivation and <c>ethers</c> for the key-to-address step. Not a strong independence claim,
    /// since the same person wrote both; what it does catch is the realistic failure, which is a
    /// transcription or encoding slip between the prose and the code -- the label, the separator,
    /// the counter's place in it, the UTF-8, the HMAC's key-and-data order.
    ///
    /// <para>
    /// The four ids are deliberate: the first two players a fresh hotel creates, an arbitrary one,
    /// and <c>int.MaxValue</c>, because the id is formatted into the HMAC's input as text and the
    /// longest one is where a formatting or culture slip would show.
    /// </para>
    ///
    /// <para>
    /// <b>If this fails, the derivation changed and every existing wallet just became
    /// unreachable.</b> Updating these literals is the wrong reflex; confirm the change was meant,
    /// and if it was, it is a migration with a new <c>DERIVATION_LABEL</c>, not an edit here.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(1, "0x8503A8289F9435d18A2c7E5101b60dcF0cE8bfBc")]
    [InlineData(2, "0x1525F606Ba81Fcd9D2344Fe59a15434CBB37e6DC")]
    [InlineData(4711, "0x42d0a7C7c90a6b83E827B7a747f5FF50e052325e")]
    [InlineData(int.MaxValue, "0x5C3b3f5dcD3780F06837e1608a34Bf9930B954B0")]
    public void DeriveAddress_matches_an_independent_implementation_of_the_scheme(
        int playerId,
        string expected
    )
    {
        HotelWallets.DeriveAddress(SEED, playerId).Should().Be(expected);
    }

    /// <summary>
    /// The property the whole custodial model rests on: the hotel stores no key, so a player's
    /// wallet has to fall out of the seed the same way every single time. If this ever varies, every
    /// balance in the hotel is stranded at an address nothing can reach again.
    /// </summary>
    [Fact]
    public void DeriveAddress_is_stable_for_the_same_player()
    {
        HotelWallets.DeriveAddress(SEED, 4711).Should().Be(HotelWallets.DeriveAddress(SEED, 4711));
    }

    [Fact]
    public void DeriveAddress_agrees_with_the_key_it_derived()
    {
        HotelWallets
            .DeriveAddress(SEED, 4711)
            .Should()
            .Be(HotelWallets.AddressOfPrivateKey(HotelWallets.DerivePrivateKey(SEED, 4711)));
    }

    [Fact]
    public void DeriveAddress_gives_every_player_a_different_wallet()
    {
        HashSet<string> addresses = [];

        for (int playerId = 1; playerId <= 500; playerId++)
        {
            addresses.Add(HotelWallets.DeriveAddress(SEED, playerId)).Should().BeTrue();
        }

        addresses.Should().HaveCount(500);
    }

    /// <summary>
    /// Two hotels sharing a player id must not share a wallet, or the one that leaks its seed empties
    /// the other.
    /// </summary>
    [Fact]
    public void DeriveAddress_separates_two_seeds()
    {
        HotelWallets
            .DeriveAddress(SEED, 4711)
            .Should()
            .NotBe(HotelWallets.DeriveAddress(OTHER_SEED, 4711));
    }

    [Fact]
    public void DeriveAddress_answers_a_well_formed_checksummed_address()
    {
        string address = HotelWallets.DeriveAddress(SEED, 4711);

        EthereumSignature.IsAddress(address).Should().BeTrue();
        EthereumSignature.ToChecksumAddress(address).Should().Be(address);
    }

    [Fact]
    public void DerivePrivateKey_refuses_a_seed_shorter_than_the_minimum()
    {
        Action derive = () => HotelWallets.DerivePrivateKey(new byte[16], 4711);

        derive.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void DerivePrivateKey_refuses_a_player_that_cannot_exist(int playerId)
    {
        Action derive = () => HotelWallets.DerivePrivateKey(SEED, playerId);

        derive.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("not hex", false)]
    // 31 bytes: long enough to look right, short enough to weaken every key derived from it.
    [InlineData("6f1a2b3c4d5e6f708192a3b4c5d6e7f8091a2b3c4d5e6f708192a3b4c5d6e7", false)]
    [InlineData("6f1a2b3c4d5e6f708192a3b4c5d6e7f8091a2b3c4d5e6f708192a3b4c5d6e7f8", true)]
    // The 0x prefix is accepted, because a seed pasted out of any wallet tool carries one.
    [InlineData("0x6f1a2b3c4d5e6f708192a3b4c5d6e7f8091a2b3c4d5e6f708192a3b4c5d6e7f8", true)]
    // Longer than the minimum is fine: more entropy is never the problem.
    [InlineData("6f1a2b3c4d5e6f708192a3b4c5d6e7f8091a2b3c4d5e6f708192a3b4c5d6e7f80011", true)]
    public void ParseMasterSeed_accepts_only_a_usable_seed(string? configured, bool expected)
    {
        (HotelWallets.ParseMasterSeed(configured) is not null).Should().Be(expected);
    }

    [Fact]
    public void GenerateMasterSeed_produces_a_fresh_usable_seed()
    {
        byte[] first = HotelWallets.GenerateMasterSeed();
        byte[] second = HotelWallets.GenerateMasterSeed();

        first.Should().HaveCount(HotelWallets.MASTER_SEED_BYTES);
        first.Should().NotEqual(second);
        HotelWallets.ParseMasterSeed(Convert.ToHexString(first)).Should().NotBeNull();
    }
}
