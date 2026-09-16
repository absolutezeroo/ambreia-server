using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vortex.Chain.Configuration;
using Vortex.Crypto.Ethereum;
using Vortex.Primitives.Chain;

namespace Vortex.Chain;

/// <summary>
/// Reads the hotel's two contracts and sends the treasury's transactions.
///
/// <para>
/// Disabled is the default and is not a failure: with no <c>Vortex:Chain:RpcUrl</c> configured this
/// constructs, says so once at startup, and refuses every call. A hotel that wants no chain is not a
/// hotel that failed to configure one.
/// </para>
/// </summary>
public sealed class ChainService : IChainService, IDisposable
{
    private readonly ChainConfig _config;
    private readonly ILogger<ChainService> _logger;
    private readonly JsonRpcClient? _rpc;
    private readonly byte[]? _treasuryKey;

    /// <summary>
    /// Serialises everything that SENDS. A transaction carries the sender's nonce, and two
    /// transactions built concurrently read the same one from the node and then fight -- one is
    /// mined and the other is rejected as "nonce too low", which reads like a bug in whatever was
    /// unlucky rather than like contention.
    ///
    /// <para>
    /// In-process, so it holds because this hotel runs one silo (see docker-compose.yml, which says
    /// the same thing about Orleans clustering). Two processes minting from ONE treasury would need
    /// the nonce handed out by something they share; the fix then is a nonce grain, not a bigger
    /// lock. Reads do not take it -- <c>eth_call</c> has no nonce and no ordering to protect.
    /// </para>
    /// </summary>
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public ChainService(IOptions<ChainConfig> options, ILogger<ChainService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _config = options.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_config.RpcUrl))
        {
            _logger.LogInformation(
                "No chain configured ('{Section}:{Key}' is empty); on-chain features are off.",
                ChainConfig.SECTION_NAME,
                nameof(ChainConfig.RpcUrl)
            );

            return;
        }

        _treasuryKey = HotelWallets.ParsePrivateKey(_config.TreasuryPrivateKey);

        // The URL is parsed rather than trusted, and that is not symmetry for its own sake. This
        // constructor runs inside the DI container, so anything it throws does not disable the
        // feature — it fails to resolve the service, and every endpoint that asks for one answers
        // 500. A missing scheme ("vortex-chain:8545" instead of "http://vortex-chain:8545") is
        // enough, and the 500 says nothing about which setting is wrong.
        bool addressable =
            Uri.TryCreate(_config.RpcUrl, UriKind.Absolute, out Uri? endpoint)
            && (endpoint.Scheme == Uri.UriSchemeHttp || endpoint.Scheme == Uri.UriSchemeHttps);

        if (
            !addressable
            || _treasuryKey is null
            || !EthereumSignature.IsAddress(_config.CoinAddress)
            || !EthereumSignature.IsAddress(_config.CollectibleAddress)
        )
        {
            // A warning and not a startup failure, on purpose: the chain is an optional feature, and
            // half-configuring it should cost the feature rather than the hotel. It is WARN rather
            // than DEBUG because the operator plainly meant to turn it on -- they set an RPC URL.
            // Each cause is named, because "on-chain features stay off" over four settings is a
            // message that sends an operator to read all four.
            _logger.LogWarning(
                "A chain RPC URL is configured but on-chain features stay off. Check '{Section}': "
                    + "RpcUrl {RpcUrlState}, TreasuryPrivateKey {KeyState}, CoinAddress "
                    + "{CoinState}, CollectibleAddress {CollectibleState}.",
                ChainConfig.SECTION_NAME,
                addressable ? "ok" : "is not an absolute http(s) URL",
                _treasuryKey is null ? "is not 32 bytes of hex on the curve" : "ok",
                EthereumSignature.IsAddress(_config.CoinAddress) ? "ok" : "is not an address",
                EthereumSignature.IsAddress(_config.CollectibleAddress) ? "ok" : "is not an address"
            );

            _treasuryKey = null;

            return;
        }

        _rpc = new JsonRpcClient(endpoint!, TimeSpan.FromSeconds(_config.RequestTimeoutSeconds));

        TreasuryAddress = HotelWallets.AddressOfPrivateKey(_treasuryKey);

        _logger.LogInformation(
            "Chain {ChainId} at {RpcUrl}; treasury {Treasury}.",
            _config.ChainId,
            _config.RpcUrl,
            TreasuryAddress
        );
    }

    public bool Enabled => _rpc is not null && _treasuryKey is not null;

    public string? TreasuryAddress { get; }

    // Clamped rather than trusted: a negative price would credit the player for transferring, and a
    // configuration file is somewhere a minus sign can land by accident.
    public int RelicTransferFeeSilver => Math.Max(0, _config.TransferFeeSilver);

    public ChainInfo GetInfo() =>
        new(
            _config.ChainId,
            _config.CurrencyName,
            _config.CurrencySymbol,
            _config.CoinAddress,
            _config.CollectibleAddress
        );

    public bool IsPublicMethodAllowed(string? method) => PublicRpcMethods.IsAllowed(method);

    public async Task<System.Text.Json.JsonElement> ForwardPublicCallAsync(
        string method,
        IReadOnlyList<System.Text.Json.JsonElement> parameters,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(parameters);

        if (!PublicRpcMethods.IsAllowed(method))
        {
            // Refused here rather than in the endpoint, so the rule travels with the thing that
            // holds the node's address: a second caller added later cannot reach the node without
            // passing it.
            throw new InvalidOperationException(
                $"'{method}' is not on the public JSON-RPC allowlist."
            );
        }

        JsonRpcClient rpc = Started();

        // Boxed as object? because JsonRpcClient serialises whatever it is handed; a JsonElement
        // round-trips as the exact JSON the browser sent, which is what a proxy has to do — it is
        // not this hotel's business to understand the shape of every parameter it forwards.
        object?[] forwarded = new object?[parameters.Count];

        for (int index = 0; index < parameters.Count; index++)
        {
            forwarded[index] = parameters[index];
        }

        return await rpc.CallAsync(method, forwarded, ct).ConfigureAwait(false);
    }

    public async Task<BigInteger> GetCoinBalanceAsync(string address, CancellationToken ct)
    {
        byte[] result = await CallAsync(
                _config.CoinAddress,
                AbiCodec.EncodeCall("balanceOf(address)", AbiCodec.Value.Address(address)),
                ct
            )
            .ConfigureAwait(false);

        return AbiCodec.DecodeUint256(result);
    }

    public async Task<IReadOnlyList<BigInteger>> GetRelicsAsync(
        string address,
        CancellationToken ct
    )
    {
        byte[] result = await CallAsync(
                _config.CollectibleAddress,
                AbiCodec.EncodeCall("tokensOfOwner(address)", AbiCodec.Value.Address(address)),
                ct
            )
            .ConfigureAwait(false);

        return AbiCodec.DecodeUintArray(result);
    }

    public Task<string> MintCoinAsync(string to, BigInteger amount, CancellationToken ct) =>
        SendAsync(
            _config.CoinAddress,
            AbiCodec.EncodeCall(
                "mint(address,uint256)",
                AbiCodec.Value.Address(to),
                AbiCodec.Value.Uint256(amount)
            ),
            ct
        );

    public Task<string> MintRelicAsync(string to, BigInteger assetId, CancellationToken ct) =>
        SendAsync(
            _config.CollectibleAddress,
            AbiCodec.EncodeCall(
                "mint(address,uint256)",
                AbiCodec.Value.Address(to),
                AbiCodec.Value.Uint256(assetId)
            ),
            ct
        );

    // `mintBatch` exists on the contract and is deliberately not used here. One transaction for N
    // Relics has to fit under a single gas ceiling, and an enumerable ERC-721 mint costs upwards of
    // 150k each against a 300k limit -- so a batch of three would revert for being too large, and
    // raising the ceiling only moves where it breaks. One mint per transaction also means a caller
    // can reconcile against the chain per token, which is what the transfer door does.
    public Task<string> BurnRelicAsync(BigInteger assetId, CancellationToken ct) =>
        SendAsync(
            _config.CollectibleAddress,
            AbiCodec.EncodeCall("burn(uint256)", AbiCodec.Value.Uint256(assetId)),
            ct
        );

    /// <summary>A read: <c>eth_call</c> against the latest block, no transaction and no fee.</summary>
    private async Task<byte[]> CallAsync(string? contract, byte[] data, CancellationToken ct)
    {
        JsonRpcClient rpc = Started(contract);

        System.Text.Json.JsonElement result = await rpc.CallAsync(
                "eth_call",
                [new { to = contract, data = JsonRpcClient.ToHexData(data) }, "latest"],
                ct
            )
            .ConfigureAwait(false);

        return JsonRpcClient.ParseData(result);
    }

    /// <summary>
    /// A write: read the treasury's nonce, sign, publish. Answers the transaction hash, which is a
    /// receipt that the node ACCEPTED it -- not that it succeeded. A mint that reverts is mined with
    /// a failed status, and a caller that needs to know waits on the receipt.
    /// </summary>
    private async Task<string> SendAsync(string? contract, byte[] data, CancellationToken ct)
    {
        JsonRpcClient rpc = Started(contract);

        await _sendLock.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            // "pending", not "latest": the count has to include transactions this hotel has already
            // sent and the node has not mined yet, or every send after the first in a block reuses a
            // nonce that is spent.
            ulong nonce = JsonRpcClient.ParseQuantity(
                await rpc.CallAsync("eth_getTransactionCount", [TreasuryAddress, "pending"], ct)
                    .ConfigureAwait(false)
            );

            byte[] signed = EthereumTransaction.Sign(
                new EthereumTransaction.Request
                {
                    ChainId = _config.ChainId,
                    Nonce = nonce,
                    MaxPriorityFeePerGas = _config.MaxPriorityFeePerGasWei,
                    MaxFeePerGas = _config.MaxFeePerGasWei,
                    GasLimit = _config.GasLimit,
                    To = contract,
                    Data = data,
                },
                _treasuryKey!
            );

            System.Text.Json.JsonElement hash = await rpc.CallAsync(
                    "eth_sendRawTransaction",
                    [JsonRpcClient.ToHexData(signed)],
                    ct
                )
                .ConfigureAwait(false);

            string transaction = hash.GetString() ?? string.Empty;

            _logger.LogDebug(
                "Sent {Data} bytes to {Contract} as nonce {Nonce}: {Transaction}",
                data.Length,
                contract,
                nonce,
                transaction
            );

            return transaction;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// The one gate every call passes, and the order in it is the point. A disabled service has no
    /// contract addresses EITHER, so checking those first answered "no contract address" — true,
    /// unhelpful, and silent about the actual state. The reason a caller can act on is that the
    /// chain is off, so that is the one it is told.
    /// </summary>
    private JsonRpcClient Started()
    {
        if (!Enabled)
        {
            throw new InvalidOperationException(
                "No chain is configured; check IChainService.Enabled before calling."
            );
        }

        return _rpc!;
    }

    private JsonRpcClient Started(string? contract)
    {
        JsonRpcClient rpc = Started();

        if (contract is null)
        {
            throw new InvalidOperationException(
                "The chain is enabled but that contract has no configured address."
            );
        }

        return rpc;
    }

    public void Dispose()
    {
        _rpc?.Dispose();
        _sendLock.Dispose();
    }
}
