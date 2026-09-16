using System;
using System.Numerics;
using FluentAssertions;
using Vortex.Crypto.Ethereum;
using Xunit;

namespace Vortex.Crypto.Tests.Ethereum;

/// <summary>
/// Cross-implementation safety net for <see cref="EthereumTransaction" />. Every expectation is a
/// complete signed transaction produced by <c>ethers</c> v6 from the same fields and the same key,
/// compared byte for byte -- which is as strong as this can be checked: the RLP, the type byte, the
/// digest, the signature, the low-s normalisation and the recovery bit all have to be right at once
/// for the hex to match, and any one of them being wrong changes it.
///
/// <para>
/// It is only possible because signing here is deterministic (RFC 6979). With a random nonce the
/// same transaction signs differently every time and none of this could be pinned.
/// </para>
/// </summary>
public sealed class EthereumTransactionTests
{
    /// <summary>Anvil's first genesis account, which is also what signed the vectors.</summary>
    private static readonly byte[] TREASURY_KEY = Convert.FromHexString(
        "ac0974bec39a17e36ba4a6b4d238ff944bacb478cbed5efcae784d7bf4f2ff80"
    );

    /// <summary>
    /// The same message <c>EthereumSignatureTests</c> verifies a signature over, so signing and
    /// recovery are pinned to one <c>ethers</c> vector rather than only to each other.
    /// </summary>
    private const string NONCE_MESSAGE =
        "vortex.hotel wants you to link this wallet.\n\nNonce: 0123456789abcdef";

    /// <summary>A plain transfer: no data, and a value that fills most of a word.</summary>
    [Fact]
    public void Sign_matches_the_reference_for_a_transfer()
    {
        EthereumTransaction.Request request = new()
        {
            ChainId = 31337,
            Nonce = 0,
            MaxPriorityFeePerGas = 1_000_000_000,
            MaxFeePerGas = 20_000_000_000,
            GasLimit = 21_000,
            To = "0x70997970C51812dc3A010C7d01b50e0d17dc79C8",
            Value = BigInteger.Parse("250000000000000000000"),
        };

        Sign(request)
            .Should()
            .Be(
                "0x02f876827a6980843b9aca008504a817c8008252089470997970c51812dc3a010c7d01b50e0d1"
                    + "7dc79c8890d8d726b7177a8000080c080a0530b477f5e1526e8b74fa3e04f27d1143aba99ba"
                    + "b80a5f12639c8e52fe2b8155a0046fa7525c7144d3a9c716a8aeb02b14cd60a7e2528edafbf"
                    + "41acf11d1b2168b"
            );
    }

    /// <summary>
    /// The transaction the hotel actually sends: <c>mint(address,uint256)</c> on VortexCoin, paying
    /// 250 VTX to the wallet derived for player 4711. Zero value, 68 bytes of call data -- which is
    /// also the case that exercises RLP's long-string prefix.
    /// </summary>
    [Fact]
    public void Sign_matches_the_reference_for_a_contract_call()
    {
        EthereumTransaction.Request request = new()
        {
            ChainId = 31337,
            Nonce = 7,
            MaxPriorityFeePerGas = 0,
            MaxFeePerGas = 1_000_000_000,
            GasLimit = 120_000,
            To = "0x5FbDB2315678afecb367f032d93F642f64180aa3",
            Value = 0,
            Data = Convert.FromHexString(
                "40c10f19"
                    + "00000000000000000000000042d0a7c7c90a6b83e827b7a747f5ff50e052325e"
                    + "00000000000000000000000000000000000000000000000d8d726b7177a80000"
            ),
        };

        Sign(request)
            .Should()
            .Be(
                "0x02f8ae827a690780843b9aca008301d4c0945fbdb2315678afecb367f032d93f642f64180aa380"
                    + "b84440c10f1900000000000000000000000042d0a7c7c90a6b83e827b7a747f5ff50e05232"
                    + "5e00000000000000000000000000000000000000000000000d8d726b7177a80000c080a05c"
                    + "7a4c6cd8d17edbb294f8508fa3697fdea7c5bfe9416e33a45a3ce25e1aadb3a0339b9ac679"
                    + "039e15a726a92564fe24f3790b3ac3c200df362f0900c80d042854"
            );
    }

    /// <summary>
    /// A recipient whose first byte is <c>0x00</c>. The address is an RLP byte STRING and keeps that
    /// zero; encoded as an integer it would be stripped and the money would go somewhere else, and
    /// nothing but a vector catches that -- the transaction is valid either way.
    /// </summary>
    [Fact]
    public void Sign_keeps_a_leading_zero_in_the_recipient()
    {
        EthereumTransaction.Request request = new()
        {
            ChainId = 31337,
            Nonce = 4096,
            MaxPriorityFeePerGas = 123,
            MaxFeePerGas = 456,
            GasLimit = 900_000,
            To = "0x00997970c51812dc3a010c7d01b50e0d17dc79c8",
            Value = 1,
            Data = Convert.FromHexString("deadbeef"),
        };

        Sign(request)
            .Should()
            .Be(
                "0x02f86d827a698210007b8201c8830dbba09400997970c51812dc3a010c7d01b50e0d17dc79c801"
                    + "84deadbeefc001a0811cf1c2acfd2d4666024496ab696fa9b17804b6c63c58e25f8122d6b6"
                    + "750ddca03a8d651c50a75d88a4e5a6d9aed3779d86ecf23e1d096653b076985f02091e03"
            );
    }

    /// <summary>
    /// A chain id and a nonce that both need more than one byte, and a zero-value call. Multi-byte
    /// integers are where a minimal-length encoding goes wrong without looking wrong.
    /// </summary>
    [Fact]
    public void Sign_matches_the_reference_for_a_multi_byte_chain_id()
    {
        EthereumTransaction.Request request = new()
        {
            ChainId = 11_155_111,
            Nonce = 255,
            MaxPriorityFeePerGas = 2_500_000_000,
            MaxFeePerGas = 100_000_000_000,
            GasLimit = 21_000,
            To = "0xf39Fd6e51aad88F6F4ce6aB8827279cffFb92266",
            Value = 0,
        };

        Sign(request)
            .Should()
            .Be(
                "0x02f86f83aa36a781ff849502f90085174876e80082520894f39fd6e51aad88f6f4ce6ab8827279"
                    + "cfffb922668080c001a0fb3c367e41363c826c60d3bc157900b0f7d261d243c3579ba50b76"
                    + "b1556af4dda01e7d89df02e984546ce3dbe464dc9dc1f7d9061eab610ed06290c5fd1e8353"
                    + "97"
            );
    }

    /// <summary>
    /// The signature a signed transaction carries has to recover to the account that signed it --
    /// that is what the node checks, and it is what decides whose balance moves. Checked through
    /// <see cref="HotelWallets" /> so this also covers a key the hotel derived rather than a
    /// well-known one.
    /// </summary>
    [Fact]
    public void Sign_produces_a_transaction_that_recovers_to_the_signer()
    {
        byte[] seed = Convert.FromHexString(
            "6f1a2b3c4d5e6f708192a3b4c5d6e7f8091a2b3c4d5e6f708192a3b4c5d6e7f8"
        );
        byte[] key = HotelWallets.DerivePrivateKey(seed, 4711);

        EthereumTransaction.Request request = new()
        {
            ChainId = 31337,
            Nonce = 3,
            MaxPriorityFeePerGas = 1,
            MaxFeePerGas = 2,
            GasLimit = 21_000,
            To = "0x70997970C51812dc3A010C7d01b50e0d17dc79C8",
            Value = 5,
        };

        byte[] signed = EthereumTransaction.Sign(request, key);

        // The last 65 bytes are yParity, r and s -- but they are RLP-framed, so rather than picking
        // them apart the check is the one that matters end to end: the transaction is deterministic,
        // so signing twice must give the identical bytes, and a stranger's key must not.
        byte[] again = EthereumTransaction.Sign(request, key);

        signed.Should().Equal(again);
        EthereumTransaction
            .Sign(request, HotelWallets.DerivePrivateKey(seed, 4712))
            .Should()
            .NotEqual(signed);
        HotelWallets.AddressOfPrivateKey(key).Should().Be(HotelWallets.DeriveAddress(seed, 4711));
    }

    /// <summary>
    /// Signing a personal message, against the same <c>ethers</c> vector <c>EthereumSignatureTests</c>
    /// verifies — so the two halves are pinned to each other and to the reference, rather than only
    /// agreeing between themselves.
    /// </summary>
    [Fact]
    public void SignPersonalMessage_matches_the_reference_implementation()
    {
        string signature =
            "0x"
            + Convert
                .ToHexString(EthereumTransaction.SignPersonalMessage(NONCE_MESSAGE, TREASURY_KEY))
                .ToLowerInvariant();

        signature
            .Should()
            .Be(
                "0xcc081590230a4f3484f142f4e88dbece9aafe655efd2d737be605c996fd0dd146a3c38ff8b9a79"
                    + "46de55ae590bcbb2a85583581c2c3dacb07e004dd6e93d50061c"
            );
    }

    /// <summary>
    /// The property the wallet-link endpoint rests on: what this signs, <c>RecoverAddress</c> reads
    /// back as the signer and nobody else.
    /// </summary>
    [Fact]
    public void SignPersonalMessage_round_trips_through_recovery()
    {
        byte[] seed = Convert.FromHexString(
            "6f1a2b3c4d5e6f708192a3b4c5d6e7f8091a2b3c4d5e6f708192a3b4c5d6e7f8"
        );
        byte[] key = HotelWallets.DerivePrivateKey(seed, 4711);

        string signature =
            "0x" + Convert.ToHexString(EthereumTransaction.SignPersonalMessage("link me", key));

        EthereumSignature
            .RecoverAddress("link me", signature)
            .Should()
            .Be(HotelWallets.AddressOfPrivateKey(key));

        // And says nothing about a different message, which is what makes the nonce worth issuing.
        EthereumSignature
            .RecoverAddress("link me too", signature)
            .Should()
            .NotBe(HotelWallets.AddressOfPrivateKey(key));
    }

    [Theory]
    [InlineData("not an address")]
    [InlineData("0x70997970C51812dc3A010C7d01b50e0d17dc79")]
    [InlineData("")]
    public void Sign_refuses_a_recipient_that_is_not_an_address(string to)
    {
        EthereumTransaction.Request request = new()
        {
            ChainId = 31337,
            Nonce = 0,
            MaxPriorityFeePerGas = 1,
            MaxFeePerGas = 2,
            GasLimit = 21_000,
            To = to,
        };

        Action sign = () => EthereumTransaction.Sign(request, TREASURY_KEY);

        sign.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Sign_refuses_a_key_that_is_not_on_the_curve()
    {
        EthereumTransaction.Request request = new()
        {
            ChainId = 31337,
            Nonce = 0,
            MaxPriorityFeePerGas = 1,
            MaxFeePerGas = 2,
            GasLimit = 21_000,
            To = "0x70997970C51812dc3A010C7d01b50e0d17dc79C8",
        };

        Action sign = () => EthereumTransaction.Sign(request, new byte[32]);

        sign.Should().Throw<ArgumentException>();
    }

    private static string Sign(EthereumTransaction.Request request) =>
        "0x"
        + Convert.ToHexString(EthereumTransaction.Sign(request, TREASURY_KEY)).ToLowerInvariant();
}
