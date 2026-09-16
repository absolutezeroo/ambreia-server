using System;
using System.Text;
using FluentAssertions;
using Vortex.Crypto.Ethereum;
using Xunit;

namespace Vortex.Crypto.Tests.Ethereum;

/// <summary>
/// Cross-implementation safety net for <see cref="EthereumSignature" />. Every expectation below was
/// produced by <c>ethers</c> v6 -- the reference JavaScript implementation, and the one the browser
/// half of this feature signs with -- and not by running the class under test and writing down what
/// it said. That distinction is the whole value of the file: recovering an address is arithmetic
/// that fails silently, and a wrong recovery does not throw, it links a wallet to a player who does
/// not own it.
///
/// <para>
/// The three keys are Anvil's first two genesis accounts, which is what a dev will actually be
/// holding, plus one arbitrary key so nothing here can be passing by having special-cased the
/// well-known ones. The three messages cover a realistic nonce challenge, the empty string (whose
/// EIP-191 length prefix is <c>"0"</c>) and a multi-byte one -- the prefix counts BYTES, and
/// counting characters is a mistake that only shows up outside ASCII.
/// </para>
///
/// <para>
/// The multi-byte message's signatures were produced by signing the literal READ BACK OUT OF THIS
/// FILE, not by typing the message into the generator a second time. The first attempt did type it
/// twice and signed <c>"accentue" + U+0301</c> (decomposed) against a file holding <c>"accentu" +
/// U+00E9</c> (precomposed): the same three glyphs, one byte apart, and three failing tests that
/// read exactly like a broken recovery. Nothing here normalises Unicode, and nothing should -- a
/// wallet signs the bytes it was handed, so the test has to sign those same bytes.
/// </para>
/// </summary>
public sealed class EthereumSignatureTests
{
    private const string ANVIL_ACCOUNT_0 = "0xf39Fd6e51aad88F6F4ce6aB8827279cffFb92266";
    private const string ANVIL_ACCOUNT_1 = "0x70997970C51812dc3A010C7d01b50e0d17dc79C8";
    private const string ARBITRARY_ACCOUNT = "0x2c7536E3605D9C16a7a3D7b1898e529396a65c23";

    private const string NONCE_MESSAGE =
        "vortex.hotel wants you to link this wallet.\n\nNonce: 0123456789abcdef";

    private const string MULTIBYTE_MESSAGE = "accentué éàü — emoji 🐛";

    [Theory]
    // Anvil account 0.
    [InlineData(
        ANVIL_ACCOUNT_0,
        NONCE_MESSAGE,
        "0xcc081590230a4f3484f142f4e88dbece9aafe655efd2d737be605c996fd0dd146a3c38ff8b9a7946de55ae590bcbb2a85583581c2c3dacb07e004dd6e93d50061c"
    )]
    [InlineData(
        ANVIL_ACCOUNT_0,
        "",
        "0xc1977b761f1dd36c29795783460d241885c8e7f9d962dbe7bba2753fd94e89b444a1cd9ed855dd09afa3b73f7c2bd097ec9abc2d2775d737505a02d3f0cafa591b"
    )]
    [InlineData(
        ANVIL_ACCOUNT_0,
        MULTIBYTE_MESSAGE,
        "0xca622f95e0534261595ed797dd3e54f6c04e143e907a2b6b91ff57adac792dc426f634c6ed81df27c9cc7cc579773fe304ef7f48e7a343d10a8c25fb805774511c"
    )]
    // Anvil account 1.
    [InlineData(
        ANVIL_ACCOUNT_1,
        NONCE_MESSAGE,
        "0xe1923bb1885da3a177cf0f7ca257e0b62680c0012b7850dfb75dbe7d523894ca68ba93c8f7191bcf3a18aacf03ff6dd0041782f9422dbd44d9bac176902d50d31b"
    )]
    [InlineData(
        ANVIL_ACCOUNT_1,
        "",
        "0xc18322a726b4f865da04323fe77b713df8286e67ae6f50fe10aa54519fddaa236c14dadd6b82cedfd527bf3c20dac250a004e3a614095f85c106068f46b3ad431c"
    )]
    [InlineData(
        ANVIL_ACCOUNT_1,
        MULTIBYTE_MESSAGE,
        "0x58b4b6eb669f9c5d71e72d10e392b9fd72f1b77b37b9a9d2c69c1bd604439926288636a9aa0deed55e4464248cb7fc1901cb423c8fdb16496b551b5147cc9b5c1b"
    )]
    // A key that is nobody's default.
    [InlineData(
        ARBITRARY_ACCOUNT,
        NONCE_MESSAGE,
        "0x65ca8e1daa913e1477d855bbaecc384fcd753b8f4c95b851473d90d10b4ae6d174e3d725c39c613d6f8ae536b655229929124f2662e74a227e2411d5b7da28361c"
    )]
    [InlineData(
        ARBITRARY_ACCOUNT,
        "",
        "0x8a68b4e66cd2b575338e16069d7b65f6f67c7ceae8945dccf8cb7bdb06278d933dd9c888f3444ca4698464079a067ad3cfffe96d493b8ecf56885169d0fdfe7d1b"
    )]
    [InlineData(
        ARBITRARY_ACCOUNT,
        MULTIBYTE_MESSAGE,
        "0xe37d2e1c14750f387a7087d122607730cae9efd8b7adc76ccd77aed73294f86f65a02992a1e5b74d0929a1664734b81a69f501ce8fb72a0ce49a27e31de3af371b"
    )]
    public void RecoverAddress_returns_the_signer(string expected, string message, string signature)
    {
        EthereumSignature.RecoverAddress(message, signature).Should().Be(expected);
    }

    /// <summary>
    /// The property the whole link endpoint rests on: sign one thing, and the signature proves only
    /// that thing. A nonce that could be reused against a different message would let a signature
    /// captured anywhere link somebody else's wallet.
    /// </summary>
    [Fact]
    public void RecoverAddress_does_not_return_the_signer_for_a_different_message()
    {
        const string signature =
            "0xcc081590230a4f3484f142f4e88dbece9aafe655efd2d737be605c996fd0dd146a3c38ff8b9a7946de55ae590bcbb2a85583581c2c3dacb07e004dd6e93d50061c";

        // One character of the nonce changed. Recovery still succeeds -- that is how ECDSA recovery
        // works, every signature recovers SOME key -- and what it recovers is a stranger.
        string? recovered = EthereumSignature.RecoverAddress(
            NONCE_MESSAGE.Replace("0123456789abcdef", "0123456789abcdee", StringComparison.Ordinal),
            signature
        );

        recovered.Should().NotBe(ANVIL_ACCOUNT_0);
    }

    /// <summary>
    /// EIP-2. <c>ethers</c> refuses to even serialize the high-s twin ("non-canonical s"), so this
    /// vector had to be assembled by hand from <c>r</c>, <c>n - s</c> and the flipped <c>v</c>. It
    /// recovers the same address as the canonical one, which is exactly why it is refused: accepted,
    /// it would be a second valid proof of the same authorisation.
    /// </summary>
    [Fact]
    public void RecoverAddress_rejects_a_malleated_signature()
    {
        const string malleated =
            "0xcc081590230a4f3484f142f4e88dbece9aafe655efd2d737be605c996fd0dd1495c3c700746586b921aa51a6f4344d56652b84ca830af38b41d210b5e6f8f13b1b";

        EthereumSignature.RecoverAddress(NONCE_MESSAGE, malleated).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0x")]
    [InlineData("not hex at all")]
    // 64 bytes: a signature with its recovery byte missing, which is not a short one to pad.
    [InlineData(
        "0xcc081590230a4f3484f142f4e88dbece9aafe655efd2d737be605c996fd0dd146a3c38ff8b9a7946de55ae590bcbb2a85583581c2c3dacb07e004dd6e93d5006"
    )]
    // A `v` that is neither 0/1 nor 27/28.
    [InlineData(
        "0xcc081590230a4f3484f142f4e88dbece9aafe655efd2d737be605c996fd0dd146a3c38ff8b9a7946de55ae590bcbb2a85583581c2c3dacb07e004dd6e93d500607"
    )]
    // r = 0, which names no point.
    [InlineData(
        "0x00000000000000000000000000000000000000000000000000000000000000006a3c38ff8b9a7946de55ae590bcbb2a85583581c2c3dacb07e004dd6e93d50061c"
    )]
    public void RecoverAddress_answers_null_for_a_malformed_signature(string? signature)
    {
        EthereumSignature.RecoverAddress(NONCE_MESSAGE, signature).Should().BeNull();
    }

    /// <summary>
    /// The digest pinned on its own, so that a failure in the recovery tests above can be read as a
    /// failure of the recovery and not of the hash underneath it.
    /// </summary>
    [Fact]
    public void Keccak256_matches_the_known_vectors()
    {
        Convert
            .ToHexString(EthereumSignature.Keccak256([]))
            .ToLowerInvariant()
            .Should()
            .Be("c5d2460186f7233c927e7db2dcc703c0e500b653ca82273b7bfad8045d85a470");

        Convert
            .ToHexString(EthereumSignature.Keccak256(Encoding.UTF8.GetBytes("abc")))
            .ToLowerInvariant()
            .Should()
            .Be("4e03657aea45a94fc7d47ba826c8d667c0d1e6e33a64a036ec44f58fa12d6c45");
    }

    /// <summary><c>ethers</c>' own <c>hashMessage("hello")</c>.</summary>
    [Fact]
    public void HashPersonalMessage_matches_the_reference_implementation()
    {
        Convert
            .ToHexString(EthereumSignature.HashPersonalMessage("hello"))
            .ToLowerInvariant()
            .Should()
            .Be("50b2c43fd39106bafbba0da34fc430e1f91e3c96ea2acee2bc34119f92b37750");
    }

    [Theory]
    [InlineData(
        "0xf39fd6e51aad88f6f4ce6ab8827279cfffb92266",
        "0xf39Fd6e51aad88F6F4ce6aB8827279cffFb92266"
    )]
    // Already checksummed: the function is idempotent, which is what lets a caller normalise without
    // first asking which form it has.
    [InlineData(
        "0xf39Fd6e51aad88F6F4ce6aB8827279cffFb92266",
        "0xf39Fd6e51aad88F6F4ce6aB8827279cffFb92266"
    )]
    // No letters at all: nothing to case, and the checksum must leave it exactly as it is.
    [InlineData(
        "0x1234567890123456789012345678901234567890",
        "0x1234567890123456789012345678901234567890"
    )]
    public void ToChecksumAddress_matches_the_reference_implementation(
        string input,
        string expected
    )
    {
        EthereumSignature.ToChecksumAddress(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("0xf39fd6e51aad88f6f4ce6ab8827279cfffb92266", true)]
    [InlineData("0xf39Fd6e51aad88F6F4ce6aB8827279cffFb92266", true)]
    // Without the prefix: a wallet may hand one over either way.
    [InlineData("f39fd6e51aad88f6f4ce6ab8827279cfffb92266", true)]
    [InlineData("0xf39fd6e51aad88f6f4ce6ab8827279cfffb922", false)]
    [InlineData("0xf39fd6e51aad88f6f4ce6ab8827279cfffb9226666", false)]
    [InlineData("0xzzzzd6e51aad88f6f4ce6ab8827279cfffb92266", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsAddress_accepts_exactly_twenty_bytes_of_hex(string? candidate, bool expected)
    {
        EthereumSignature.IsAddress(candidate).Should().Be(expected);
    }
}
