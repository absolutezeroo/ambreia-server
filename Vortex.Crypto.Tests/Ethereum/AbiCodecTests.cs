using System;
using System.Collections.Generic;
using System.Numerics;
using FluentAssertions;
using Vortex.Crypto.Ethereum;
using Xunit;

namespace Vortex.Crypto.Tests.Ethereum;

/// <summary>
/// Cross-implementation safety net for <see cref="AbiCodec" />, against <c>ethers</c>' own
/// <c>Interface.encodeFunctionData</c> and <c>AbiCoder</c>.
///
/// <para>
/// Worth checking rather than eyeballing because every mistake this can make produces bytes the
/// contract accepts. A selector off by one hash is a call to a different function; an argument in
/// the wrong word is a different amount to a different account; a dynamic offset counted from the
/// wrong place shifts every value after it. None of that reverts — it just does something else.
/// </para>
/// </summary>
public sealed class AbiCodecTests
{
    private const string PLAYER = "0x42d0a7C7c90a6b83E827B7a747f5FF50e052325e";
    private const string OTHER = "0x70997970C51812dc3A010C7d01b50e0d17dc79C8";

    [Theory]
    [InlineData("mint(address,uint256)", "40c10f19")]
    [InlineData("balanceOf(address)", "70a08231")]
    [InlineData("tokensOfOwner(address)", "8462151c")]
    [InlineData("ownerOf(uint256)", "6352211e")]
    public void Selector_matches_the_reference_implementation(string signature, string expected)
    {
        Hex(AbiCodec.Selector(signature)).Should().Be(expected);
    }

    /// <summary>The call the hotel makes to pay a player.</summary>
    [Fact]
    public void EncodeCall_matches_the_reference_for_two_static_arguments()
    {
        Hex(
                AbiCodec.EncodeCall(
                    "mint(address,uint256)",
                    AbiCodec.Value.Address(PLAYER),
                    AbiCodec.Value.Uint256(BigInteger.Parse("250000000000000000000"))
                )
            )
            .Should()
            .Be(
                "40c10f19"
                    + "00000000000000000000000042d0a7c7c90a6b83e827b7a747f5ff50e052325e"
                    + "00000000000000000000000000000000000000000000000d8d726b7177a80000"
            );
    }

    [Fact]
    public void EncodeCall_matches_the_reference_for_one_argument()
    {
        Hex(AbiCodec.EncodeCall("balanceOf(address)", AbiCodec.Value.Address(PLAYER)))
            .Should()
            .Be("70a08231" + "00000000000000000000000042d0a7c7c90a6b83e827b7a747f5ff50e052325e");
    }

    /// <summary>
    /// Two dynamic arguments, which is where the head-and-tail split actually has to be right: the
    /// second array's offset depends on how long the first one turned out to be.
    /// </summary>
    [Fact]
    public void EncodeCall_matches_the_reference_for_two_dynamic_arguments()
    {
        Hex(
                AbiCodec.EncodeCall(
                    "mintBatch(address[],uint256[])",
                    AbiCodec.Value.AddressArray([PLAYER, OTHER]),
                    AbiCodec.Value.UintArray([new BigInteger(100), new BigInteger(50)])
                )
            )
            .Should()
            .Be(
                "7c88e3d9"
                    + "0000000000000000000000000000000000000000000000000000000000000040"
                    + "00000000000000000000000000000000000000000000000000000000000000a0"
                    + "0000000000000000000000000000000000000000000000000000000000000002"
                    + "00000000000000000000000042d0a7c7c90a6b83e827b7a747f5ff50e052325e"
                    + "00000000000000000000000070997970c51812dc3a010c7d01b50e0d17dc79c8"
                    + "0000000000000000000000000000000000000000000000000000000000000002"
                    + "0000000000000000000000000000000000000000000000000000000000000064"
                    + "0000000000000000000000000000000000000000000000000000000000000032"
            );
    }

    /// <summary>
    /// A static argument followed by a dynamic one -- the collectible's batch mint. The offset is
    /// counted from the start of the ARGUMENTS, not from the start of the call data, so a selector
    /// counted in would put it four bytes out.
    /// </summary>
    [Fact]
    public void EncodeCall_matches_the_reference_for_a_static_then_dynamic_argument()
    {
        Hex(
                AbiCodec.EncodeCall(
                    "mintBatch(address,uint256[])",
                    AbiCodec.Value.Address(PLAYER),
                    AbiCodec.Value.UintArray([new BigInteger(90714), new BigInteger(90715)])
                )
            )
            .Should()
            .Be(
                "75ceb341"
                    + "00000000000000000000000042d0a7c7c90a6b83e827b7a747f5ff50e052325e"
                    + "0000000000000000000000000000000000000000000000000000000000000040"
                    + "0000000000000000000000000000000000000000000000000000000000000002"
                    + "000000000000000000000000000000000000000000000000000000000001625a"
                    + "000000000000000000000000000000000000000000000000000000000001625b"
            );
    }

    [Fact]
    public void DecodeUint256_reads_a_balance()
    {
        AbiCodec
            .DecodeUint256(
                Bytes("00000000000000000000000000000000000000000000000d8d726b7177a80000")
            )
            .Should()
            .Be(BigInteger.Parse("250000000000000000000"));
    }

    /// <summary>The last 20 bytes of the word, not the first.</summary>
    [Fact]
    public void DecodeAddress_reads_the_right_end_of_the_word()
    {
        AbiCodec
            .DecodeAddress(
                Bytes("00000000000000000000000042d0a7c7c90a6b83e827b7a747f5ff50e052325e")
            )
            .Should()
            .Be(PLAYER);
    }

    /// <summary>
    /// A returned array is dynamic: word 0 is the OFFSET and word 1 is the count. Reading the count
    /// from word 0 gives 32 here — a list of thirty-two ids that do not exist.
    /// </summary>
    [Fact]
    public void DecodeUintArray_reads_a_wallet_holding_two_relics()
    {
        AbiCodec
            .DecodeUintArray(
                Bytes(
                    "0000000000000000000000000000000000000000000000000000000000000020"
                        + "0000000000000000000000000000000000000000000000000000000000000002"
                        + "000000000000000000000000000000000000000000000000000000000001625a"
                        + "000000000000000000000000000000000000000000000000000000000001625b"
                )
            )
            .Should()
            .Equal(new BigInteger(90714), new BigInteger(90715));
    }

    [Fact]
    public void DecodeUintArray_reads_an_empty_wallet()
    {
        AbiCodec
            .DecodeUintArray(
                Bytes(
                    "0000000000000000000000000000000000000000000000000000000000000020"
                        + "0000000000000000000000000000000000000000000000000000000000000000"
                )
            )
            .Should()
            .BeEmpty();
    }

    /// <summary>
    /// The length decides where the string ends, not the padding. 0x33 is 51 bytes in a 64-byte
    /// tail, so decoding the whole tail appends thirteen NULs that print as nothing and compare
    /// unequal to everything.
    /// </summary>
    [Fact]
    public void DecodeString_stops_at_the_length_and_not_at_the_padding()
    {
        AbiCodec
            .DecodeString(
                Bytes(
                    "0000000000000000000000000000000000000000000000000000000000000020"
                        + "0000000000000000000000000000000000000000000000000000000000000033"
                        + "687474703a2f2f6c6f63616c686f73743a383038302f6170692f7075626c6963"
                        + "2f636f6c6c65637469626c65732f393037313400000000000000000000000000"
                )
            )
            .Should()
            .Be("http://localhost:8080/api/public/collectibles/90714");
    }

    [Fact]
    public void DecodeBool_reads_both_answers()
    {
        AbiCodec
            .DecodeBool(Bytes("0000000000000000000000000000000000000000000000000000000000000001"))
            .Should()
            .BeTrue();
        AbiCodec
            .DecodeBool(Bytes("0000000000000000000000000000000000000000000000000000000000000000"))
            .Should()
            .BeFalse();
    }

    [Fact]
    public void Value_refuses_something_that_is_not_an_address()
    {
        Action encode = () => AbiCodec.Value.Address("0xnope");

        encode.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Value_refuses_a_negative_amount()
    {
        Action encode = () => AbiCodec.Value.Uint256(BigInteger.MinusOne);

        encode.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// 2^256 is one more than a uint256 holds. Truncating it silently would encode a completely
    /// different amount -- in this case zero.
    /// </summary>
    [Fact]
    public void Value_refuses_an_amount_that_does_not_fit()
    {
        Action encode = () => AbiCodec.Value.Uint256(BigInteger.Pow(2, 256));

        encode.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>The largest value that DOES fit, which must encode to a full word of ones.</summary>
    [Fact]
    public void Value_accepts_the_largest_uint256()
    {
        Hex(AbiCodec.Value.Uint256(BigInteger.Pow(2, 256) - 1).Encoded)
            .Should()
            .Be(new string('f', 64));
    }

    private static string Hex(IEnumerable<byte> bytes) =>
        Convert.ToHexString([.. bytes]).ToLowerInvariant();

    private static byte[] Bytes(string hex) => Convert.FromHexString(hex);
}
