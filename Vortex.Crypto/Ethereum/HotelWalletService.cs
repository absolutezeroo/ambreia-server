using System;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vortex.Crypto.Configuration;
using Vortex.Primitives.Crypto;

namespace Vortex.Crypto.Ethereum;

/// <summary>
/// Hands out each player's custodial wallet address, derived once per call from the configured
/// master seed (see <see cref="HotelWallets" />).
///
/// <para>
/// With no seed configured this falls back to the address the hotel used before real wallets
/// existed: <c>0x</c> plus the player id in hex, left-padded. That address has no key behind it and
/// never had one -- it exists because the client treats the value as an opaque key and hangs two
/// tabs on an empty one. Keeping it is what lets an existing deployment upgrade without changing
/// anything; setting a seed is the whole of turning real wallets on. The fallback is announced at
/// startup rather than taken silently, because an address that looks real and holds nothing is
/// precisely the sort of thing nobody notices until a balance is expected.
/// </para>
/// </summary>
public sealed class HotelWalletService : IHotelWalletService
{
    private readonly byte[]? _masterSeed;

    public HotelWalletService(IOptions<CryptoConfig> options, ILogger<HotelWalletService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _masterSeed = HotelWallets.ParseMasterSeed(options.Value.WalletMasterSeed);

        if (_masterSeed is null)
        {
            // The remedy is spelled out as a shell command and an environment variable, because the
            // person reading this line is running a container and cannot call a C# method — which
            // is what this used to tell them to do.
            logger.LogWarning(
                "No wallet master seed configured ('{Section}:{Key}'): players are given "
                    + "placeholder wallet addresses with no keys behind them, and every on-chain "
                    + "feature is off. Generate 32 bytes of hex — "
                    + "`openssl rand -hex 32` — and set it as {EnvironmentVariable}. "
                    + "Keep it: it is every player's private key, and losing it strands every "
                    + "balance on those addresses.",
                CryptoConfig.SECTION_NAME,
                nameof(CryptoConfig.WalletMasterSeed),
                "VORTEX__Vortex__Crypto__WalletMasterSeed"
            );
        }
        else
        {
            logger.LogInformation("Custodial player wallets are enabled.");
        }
    }

    public bool Enabled => _masterSeed is not null;

    public string GetAddress(int playerId)
    {
        if (playerId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(playerId),
                playerId,
                "A wallet is derived for a real player; there is no player 0."
            );
        }

        return _masterSeed is null
            ? "0x" + playerId.ToString("x", CultureInfo.InvariantCulture).PadLeft(40, '0')
            : HotelWallets.DeriveAddress(_masterSeed, playerId);
    }
}
