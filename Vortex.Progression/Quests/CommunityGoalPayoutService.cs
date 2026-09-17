using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Database.Entities.Quests;
using Vortex.Primitives.Chain;

namespace Vortex.Progression.Quests;

/// <summary>
/// Mints the VortexCoin owed by community goal rungs, one row of <c>community_goal_payouts</c> at a
/// time.
/// </summary>
/// <remarks>
/// <para>
/// This is the only place in the hotel where coins are created by the game rather than by an
/// operator, and it is deliberately the dullest code in the feature: it reads a row, calls
/// <c>MintCoinAsync</c>, writes the transaction hash back. Everything that decides <i>who</i> and
/// <i>how much</i> happened when the rung fell — see <c>CommunityGoalGrain.AwardClearedRungsAsync</c>
/// — and is already frozen in the row.
/// </para>
/// <para>
/// <b>It is separate from the grain because a mint is an Ethereum transaction.</b> Seconds of
/// latency, and it can fail for reasons that have nothing to do with the hotel — a node restart, an
/// out-of-gas, a nonce clash with the Relic doors. A transient chain failure must not lose a reward,
/// so failure here is just a row that stays unpaid and is tried again on the next pass, with
/// <c>attempts</c> and <c>last_error</c> left on it so a campaign that is quietly failing shows up
/// in the table.
/// </para>
/// <para>
/// One row at a time and saved after each: minting is not batched because the two failure modes
/// differ — a batch that half-succeeds leaves no way to tell which half, and the hotel would either
/// pay twice or not at all. The volume makes that affordable: a rung pays at most
/// <c>reward_user_limit</c> players and a campaign has a handful of rungs, so the whole season is
/// tens of transactions, not thousands.
/// </para>
/// </remarks>
internal sealed class CommunityGoalPayoutService(
    IDbContextFactory<VortexDbContext> dbContextFactory,
    IChainRewardService coins,
    ILogger<CommunityGoalPayoutService> logger
) : BackgroundService
{
    /// <summary>
    /// How often the queue is drained. A minute rather than seconds because nothing is waiting on
    /// it: the rung has already been announced to the hotel by the progress widget, and the coins
    /// land in a wallet the player reads on a web page.
    /// </summary>
    private static readonly TimeSpan DRAIN_INTERVAL = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How many rewards one pass will mint. A ceiling on how long a single pass can hold the chain,
    /// not a limit on the campaign — whatever is left is taken on the next tick a minute later.
    /// </summary>
    private const int MAX_PER_PASS = 25;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!coins.Enabled)
        {
            // A hotel with no chain runs community goals perfectly well — they just award nothing on
            // one. Not a warning: the grain already logs when a rung wanted coins and could not pay.
            logger.LogDebug(
                "No chain or no custodial wallets; community goal coin rewards are not minted."
            );

            return;
        }

        using PeriodicTimer timer = new(DRAIN_INTERVAL);

        // Once before the first tick, so rewards owed while the hotel was down are not held for a
        // further minute after it comes back.
        await DrainAsync(stoppingToken).ConfigureAwait(false);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await DrainAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task DrainAsync(CancellationToken ct)
    {
        try
        {
            await using VortexDbContext db = await dbContextFactory
                .CreateDbContextAsync(ct)
                .ConfigureAwait(false);

            List<CommunityGoalPayoutEntity> owed = await db
                .CommunityGoalPayouts.Where(p => p.MintedAt == null)
                .OrderBy(p => p.Id)
                .Take(MAX_PER_PASS)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            foreach (CommunityGoalPayoutEntity payout in owed)
            {
                ct.ThrowIfCancellationRequested();

                await MintOneAsync(db, payout, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown. Whatever is left is still owed, and the next start takes it.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Draining the community goal payout queue failed.");
        }
    }

    private async Task MintOneAsync(
        VortexDbContext db,
        CommunityGoalPayoutEntity payout,
        CancellationToken ct
    )
    {
        payout.Attempts += 1;

        try
        {
            // Paid through the player id, like every other coin reward in the hotel, so that one
            // place decides which address a player's coins land at. The row's own `address` column
            // is the record of where they were MEANT to go, resolved when the rung fell; the two can
            // only differ if the hotel's master seed was replaced mid-campaign, and then the row is
            // the evidence of it.
            string hash = await coins
                .PayAsync(payout.PlayerEntityId, payout.AmountCoins, ct)
                .ConfigureAwait(false);

            payout.TransactionHash = hash;
            payout.MintedAt = DateTime.UtcNow;
            payout.LastError = null;

            logger.LogInformation(
                "Paid {Amount} VTX to player {PlayerId} for community goal rung {Level} ({Hash}).",
                payout.AmountCoins,
                payout.PlayerEntityId,
                payout.LevelNumber,
                hash
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Truncated to the column: an RPC error can carry a whole revert trace, and a write that
            // throws because the diagnostic did not fit would turn a failed payment into a failed
            // pass — losing the attempt count that is the only sign this is happening at all.
            payout.LastError = ex.Message.Length > 512 ? ex.Message[..512] : ex.Message;

            logger.LogError(
                ex,
                "Minting {Amount} VTX for player {PlayerId} (community goal rung {Level}) failed; "
                    + "attempt {Attempts}. It stays owed.",
                payout.AmountCoins,
                payout.PlayerEntityId,
                payout.LevelNumber,
                payout.Attempts
            );
        }

        // Saved either way, and per row: a success must be recorded before the next mint is
        // attempted, or a crash mid-pass would re-mint everything this pass already paid.
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
