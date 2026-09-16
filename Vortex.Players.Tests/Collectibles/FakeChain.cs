using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Chain;

namespace Vortex.Players.Tests.Collectibles;

/// <summary>
///     A chain that remembers which wallet holds which Relic, and nothing else.
/// </summary>
/// <remarks>
///     It answers <c>tokensOfOwner</c> out of a dictionary, which is enough for both doors: the
///     transfer out reads a wallet, mints into it and reads it again, and the claim back reads the
///     treasury, burns and reads it again. What the tests are checking is that the hotel's records
///     follow those reads — never the send.
/// </remarks>
internal sealed class FakeChain : IChainService
{
    public const string Treasury = "0x00000000000000000000000000000000000f1a5c";

    private readonly Dictionary<string, HashSet<BigInteger>> _wallets = new(
        StringComparer.OrdinalIgnoreCase
    );

    public bool Enabled { get; init; } = true;

    public string? TreasuryAddress { get; init; } = Treasury;

    public int RelicTransferFeeSilver { get; init; }

    /// <summary>Every id <see cref="BurnRelicAsync" /> was asked to destroy, in order.</summary>
    public List<BigInteger> Burned { get; } = [];

    /// <summary>Puts a token in a wallet without minting it, to set a test up.</summary>
    public void Give(string address, params int[] assetIds)
    {
        foreach (int assetId in assetIds)
        {
            Held(address).Add(assetId);
        }
    }

    public IReadOnlyCollection<BigInteger> Holdings(string address) => Held(address);

    public ChainInfo GetInfo() => new(31337, "Vortex", "VTX", "0xcoin", "0xcollectible");

    public bool IsPublicMethodAllowed(string? method) => false;

    public Task<JsonElement> ForwardPublicCallAsync(
        string method,
        IReadOnlyList<JsonElement> parameters,
        CancellationToken ct
    ) => throw new NotSupportedException();

    public Task<BigInteger> GetCoinBalanceAsync(string address, CancellationToken ct) =>
        Task.FromResult(BigInteger.Zero);

    public Task<IReadOnlyList<BigInteger>> GetRelicsAsync(string address, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<BigInteger>>([.. Held(address)]);

    public Task<string> MintCoinAsync(string to, BigInteger amount, CancellationToken ct) =>
        Task.FromResult("0xcoinmint");

    public Task<string> MintRelicAsync(string to, BigInteger assetId, CancellationToken ct)
    {
        // The contract reverts on a duplicate id, so the fake does too — that is the behaviour the
        // transfer's "skip what the wallet already holds" step exists to avoid.
        if (!Held(to).Add(assetId))
        {
            throw new InvalidOperationException($"Token {assetId} already exists.");
        }

        return Task.FromResult("0xmint");
    }

    public Task<string> BurnRelicAsync(BigInteger assetId, CancellationToken ct)
    {
        Burned.Add(assetId);
        Held(TreasuryAddress ?? Treasury).Remove(assetId);

        return Task.FromResult("0xburn");
    }

    private HashSet<BigInteger> Held(string address)
    {
        if (!_wallets.TryGetValue(address, out HashSet<BigInteger>? held))
        {
            held = [];
            _wallets[address] = held;
        }

        return held;
    }
}
