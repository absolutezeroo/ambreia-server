using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Primitives.Chain;
using Vortex.Primitives.Crypto;

namespace Vortex.Players;

/// <summary>
/// Reads who holds the most VortexCoin, on a timer, and keeps the answer.
/// </summary>
/// <remarks>
/// <para>
/// This is the contest. Everything else the chain does — the two Relic doors, the custodial wallets,
/// the coin itself — existed before anything asked the one question the chain was built for:
/// <c>GetCoinBalanceAsync</c> was implemented, tested, and called by nobody.
/// </para>
/// <para>
/// <b>One <c>eth_call</c> per player, which is why this is a timer and not a request handler.</b>
/// The standings ask the same question about every account at once; answering it per request would
/// turn a page anyone can open into a loop over the whole hotel against a node shared with the
/// mints, the claims and every wallet extension talking through the public proxy.
/// </para>
/// <para>
/// A balance only moves when an operator grants coin or a player moves it themselves, so a snapshot
/// minutes old is not a lie — provided it says how old it is, which is why
/// <see cref="TakenAtUtc" /> is on the interface rather than left for the reader to assume.
/// </para>
/// </remarks>
internal sealed class ChainLeaderboardService(
    IDbContextFactory<VortexDbContext> dbContextFactory,
    IChainService chain,
    IHotelWalletService wallets,
    ILogger<ChainLeaderboardService> logger
) : BackgroundService, IChainLeaderboard
{
    /// <summary>
    /// How often the standings are re-read. Minutes rather than seconds because a balance only
    /// changes when somebody is granted coin or moves it, and because each pass costs one call per
    /// player.
    /// </summary>
    private static readonly TimeSpan REFRESH_INTERVAL = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How many players one pass will look at, most recently seen first.
    /// </summary>
    /// <remarks>
    /// A ceiling and not a page: a contest is decided at the top, and reading every account of a
    /// hotel that has run for years to rank the fifty who play would spend the node's time on
    /// nothing. If a hotel ever outgrows this, the fix is to track who has been granted coin rather
    /// than to raise the number.
    /// </remarks>
    private const int MAX_PLAYERS_PER_PASS = 500;

    /// <summary>
    /// Not volatile, because an <see cref="ImmutableArray{T}" /> cannot be: it is a struct, and the
    /// compiler refuses (CS0677). It wraps one array reference, so replacing it is a single
    /// reference write and a reader sees either the old standings or the new ones, never a mix. A
    /// reader that briefly sees the previous snapshot is showing a table a few seconds staler than
    /// it could be, which is what <see cref="TakenAtUtc" /> is for.
    /// </summary>
    private ImmutableArray<ChainLeaderboardEntry> _standings = [];

    public ImmutableArray<ChainLeaderboardEntry> Standings => _standings;

    public DateTime? TakenAtUtc { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!chain.Enabled || !wallets.Enabled)
        {
            // Not a failure and not worth a warning: a hotel with no chain has no contest, and the
            // standings stay empty, which is what a caller renders as "nothing here".
            logger.LogDebug("No chain or no custodial wallets; the coin leaderboard stays empty.");

            return;
        }

        using PeriodicTimer timer = new(REFRESH_INTERVAL);

        // Once before the first tick, so a restart does not leave the page blank for five minutes.
        await RefreshAsync(stoppingToken).ConfigureAwait(false);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await RefreshAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        try
        {
            await using VortexDbContext db = await dbContextFactory
                .CreateDbContextAsync(ct)
                .ConfigureAwait(false);

            var players = await db
                .Players.AsNoTracking()
                .Where(player => player.DeletedAt == null)
                .OrderByDescending(player => player.LastLoginAt)
                .Take(MAX_PLAYERS_PER_PASS)
                .Select(player => new { player.Id, player.Name })
                .ToListAsync(ct)
                .ConfigureAwait(false);

            List<ChainLeaderboardEntry> held = [];

            foreach (var player in players)
            {
                ct.ThrowIfCancellationRequested();

                string address = wallets.GetAddress(player.Id);

                BigInteger balance = await chain
                    .GetCoinBalanceAsync(address, ct)
                    .ConfigureAwait(false);

                // Zero is the overwhelming majority and says nothing: a standings table listing
                // every account on nought is a table nobody reads to the end.
                if (balance.IsZero)
                {
                    continue;
                }

                held.Add(new ChainLeaderboardEntry(player.Id, player.Name, address, balance));
            }

            _standings =
            [
                .. held.OrderByDescending(entry => entry.Balance)
                    .ThenBy(entry => entry.PlayerName, StringComparer.OrdinalIgnoreCase),
            ];

            TakenAtUtc = DateTime.UtcNow;

            logger.LogDebug(
                "Coin leaderboard refreshed: {Holders} holder(s) out of {Read} player(s) read.",
                _standings.Length,
                players.Count
            );
        }
        catch (OperationCanceledException)
        {
            // The host is stopping; not an error and not something to log as one.
            throw;
        }
        catch (Exception failure)
        {
            // The previous standings are kept rather than cleared. A node that blinked should cost
            // the page its freshness, not its contents — and TakenAtUtc still says how stale it is.
            logger.LogWarning(
                failure,
                "Could not refresh the coin leaderboard; keeping the standings from {TakenAtUtc}.",
                TakenAtUtc
            );
        }
    }
}
