using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vortex.Primitives.Chain;

namespace Vortex.Chain;

/// <summary>
/// Builds <see cref="ChainService" /> while the hotel is starting, and says what it found.
/// </summary>
/// <remarks>
/// <para>
/// Without this the service is built on first use, because that is what a singleton does — and the
/// first use is a player's wallet hitting the web API, hours later and on somebody else's screen.
/// Everything the constructor has to say about the configuration went there too: the warning naming
/// a malformed setting, and the line confirming which treasury the hotel signs as. An operator
/// reading the startup log saw nothing at all about the chain, correct or not.
/// </para>
/// <para>
/// Worse, a constructor that throws — a setting bad enough to break <c>new Uri</c>, say — surfaced
/// as an HTTP 500 with an empty body, logged by the framework under a category a hotel's own log
/// filter does not usually pass. The failure was real, visible to a stranger with curl, and
/// invisible to the person who could fix it.
/// </para>
/// <para>
/// Failing here does NOT stop the hotel. The chain is an optional feature and a hotel that wants
/// none must start exactly as before; what changes is that the reason is written down once, where
/// it is read.
/// </para>
/// </remarks>
internal sealed class ChainStartupProbe(
    IServiceProvider services,
    ILogger<ChainStartupProbe> logger
) : IHostedService
{
    private readonly IServiceProvider _services = services;
    private readonly ILogger<ChainStartupProbe> _logger = logger;

    public Task StartAsync(CancellationToken ct)
    {
        try
        {
            IChainService chain = (IChainService)_services.GetService(typeof(IChainService))!;

            if (!chain.Enabled)
            {
                // ChainService has already said which setting is wrong, in its own constructor.
                return Task.CompletedTask;
            }

            ChainInfo info = chain.GetInfo();

            // The treasury is printed because it is the one value that can silently disagree with
            // the node's: both derive it from their own copy of the key, and a copy-paste that lost
            // a character gives a hotel signing as an account that owns neither contract. The node
            // prints the same line, and the two are meant to be compared.
            _logger.LogInformation(
                "Chain ready: id {ChainId}, treasury {Treasury}, coin {Coin}, collectible "
                    + "{Collectible}.",
                info.ChainId,
                chain.TreasuryAddress,
                info.CoinAddress,
                info.CollectibleAddress
            );
        }
        catch (Exception failure)
        {
            _logger.LogError(
                failure,
                "The chain service could not be built, so every chain route will answer 500 until "
                    + "this is fixed. Check 'Vortex:Chain'. The hotel is otherwise unaffected."
            );
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
