using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Database.Entities.RewardTracks;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Events;
using Vortex.Primitives.RewardTracks;
using Vortex.Primitives.RewardTracks.Snapshots;
using Vortex.Protocol.Messages.Outgoing.RewardTracks;
using Vortex.RewardTracks.Rewards;

namespace Vortex.RewardTracks.Grains;

/// <summary>
/// Claiming prizes.
/// </summary>
/// <remarks>
/// <para>
/// The client sends two strings and neither is trusted. The track, the prize, the points, the
/// premium entitlement and the claim window are all re-resolved here from content and stored state
/// before anything is handed over.
/// </para>
/// <para>
/// The claim row is written <em>before</em> the bundle is granted, and that ordering is the
/// idempotency. Its unique index means a second attempt loses the insert; the grant only runs for
/// the attempt that won. The alternative — grant then record — hands the bundle out twice whenever
/// the recording fails.
/// </para>
/// </remarks>
internal sealed partial class PlayerRewardTrackGrain
{
    public async Task<RewardClaimOutcome> ClaimPrizeAsync(
        string trackId,
        string prizeId,
        CancellationToken ct
    )
    {
        RewardClaimOutcome outcome = await ClaimInternalAsync(trackId, prizeId, ct)
            .ConfigureAwait(true);

        await Presence
            .SendComposerAsync(
                new RewardTrackClaimResultMessageComposer
                {
                    TrackId = outcome.TrackId,
                    PrizeId = outcome.PrizeId,
                    Result = outcome.Result,
                }
            )
            .ConfigureAwait(true);

        return outcome;
    }

    public async Task<ImmutableArray<RewardClaimOutcome>> ClaimAllAsync(
        string trackId,
        CancellationToken ct
    )
    {
        if (!catalog.TryGetTrack(trackId, out RewardTrackDefinitionSnapshot? track))
        {
            return
            [
                RewardClaimOutcome.Fail(RewardClaimResult.TrackNotFound, trackId, string.Empty),
            ];
        }

        List<RewardClaimOutcome> outcomes = [];

        // Deterministic order: cheapest milestone first, ties broken by the content's own sort.
        // Two players with the same points claiming everything get the same sequence, which is what
        // makes the operation reproducible when someone asks why one reward is missing.
        foreach (
            RewardTrackPrizeDefinitionSnapshot prize in track
                .Prizes.OrderBy(p => p.RequiredPoints)
                .ThenBy(p => p.SortOrder)
                .ThenBy(p => p.PrizeId, StringComparer.Ordinal)
        )
        {
            RewardClaimOutcome outcome = await ClaimInternalAsync(trackId, prize.PrizeId, ct)
                .ConfigureAwait(true);

            // Prizes that were never claimable are skipped in silence — "claim all" means "claim
            // what I can", so reporting a locked milestone as a failure would be noise.
            if (
                outcome.Result
                is RewardClaimResult.AlreadyClaimed
                    or RewardClaimResult.NotEnoughPoints
                    or RewardClaimResult.PremiumRequired
            )
            {
                continue;
            }

            outcomes.Add(outcome);

            // Each prize is its own transaction, so what has already been granted stays granted.
            // Stopping here rather than pressing on: whatever refused this one — the window closed,
            // a service is down — will refuse the next one too, and a run of failures is worse than
            // one.
            if (!outcome.Succeeded)
            {
                break;
            }
        }

        if (outcomes.Count > 0)
        {
            await PushTracksAsync(reload: false, ct).ConfigureAwait(true);
        }

        return [.. outcomes];
    }

    private async Task<RewardClaimOutcome> ClaimInternalAsync(
        string trackId,
        string prizeId,
        CancellationToken ct
    )
    {
        if (!_featureEnabled)
        {
            return RewardClaimOutcome.Fail(RewardClaimResult.Disabled, trackId, prizeId);
        }

        if (!catalog.TryGetTrack(trackId, out RewardTrackDefinitionSnapshot? track))
        {
            return RewardClaimOutcome.Fail(RewardClaimResult.TrackNotFound, trackId, prizeId);
        }

        DateTime now = DateTime.UtcNow;

        // A track past its claim window is refused as "not found" rather than with a window-specific
        // code: the client has no line for an expired track, and a wrong localized line reads worse
        // than the generic one.
        if (!track.AcceptsClaimsAt(now))
        {
            return RewardClaimOutcome.Fail(RewardClaimResult.TrackNotFound, trackId, prizeId);
        }

        RewardTrackPrizeDefinitionSnapshot? prize = FindPrize(track, prizeId);

        if (prize is null)
        {
            return RewardClaimOutcome.Fail(RewardClaimResult.RewardNotFound, trackId, prizeId);
        }

        TrackState state = EnsureTrack(trackId);

        if (state.ClaimedPrizeIds.Contains(prizeId))
        {
            return RewardClaimOutcome.Fail(RewardClaimResult.AlreadyClaimed, trackId, prizeId);
        }

        if (prize.Premium && !state.PremiumUnlocked)
        {
            return RewardClaimOutcome.Fail(RewardClaimResult.PremiumRequired, trackId, prizeId);
        }

        if (state.Points < prize.RequiredPoints)
        {
            return RewardClaimOutcome.Fail(RewardClaimResult.NotEnoughPoints, trackId, prizeId);
        }

        if (prize.Rewards.IsDefaultOrEmpty)
        {
            // Content with a milestone that hands over nothing. Recording a claim for it would burn
            // the milestone; refusing leaves it for whoever fixes the content.
            logger.LogWarning(
                "Prize {TrackId}/{PrizeId} has no rewards; player {PlayerId}'s claim was refused.",
                trackId,
                prizeId,
                PlayerId
            );

            return RewardClaimOutcome.Fail(RewardClaimResult.RewardNotFound, trackId, prizeId);
        }

        CommerceOperationId operation = CommerceOperationId.New();

        await journal
            .OpenAsync(
                operation,
                CommerceOperationKind.RewardTrackPrize,
                PlayerId,
                $"track={trackId} prize={prizeId} points={state.Points} rewards={prize.Rewards.Length}",
                ct
            )
            .ConfigureAwait(true);

        // The claim row first. Its unique index is the whole of the never-twice guarantee: a second
        // attempt -- a double click, a retried grain call, a reconnect replaying the packet -- fails
        // to insert and is answered AlreadyClaimed without granting anything.
        await using VortexDbContext db = await dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(true);

        try
        {
            db.PlayerRewardTrackClaims.Add(
                new PlayerRewardTrackClaimEntity
                {
                    PlayerEntityId = PlayerId,
                    TrackId = trackId,
                    PrizeId = prizeId,
                    ClaimedAt = now,
                    PointsAtClaim = state.Points,
                }
            );

            await db.SaveChangesAsync(ct).ConfigureAwait(true);
        }
        catch (DbUpdateException ex)
        {
            // "Almost certainly the unique index" was doing a lot of work here. Any other write
            // failure was answered AlreadyClaimed and cached as claimed, which spends an entitlement
            // that was never recorded and cannot be claimed again. Ask the table.
            bool alreadyClaimed = await db
                .PlayerRewardTrackClaims.AsNoTracking()
                .AnyAsync(
                    c =>
                        c.PlayerEntityId == PlayerId
                        && c.TrackId == trackId
                        && c.PrizeId == prizeId,
                    ct
                )
                .ConfigureAwait(true);

            if (!alreadyClaimed)
            {
                throw;
            }

            // The unique index really did do its job. The in-memory set is brought back in line so
            // the next read agrees with the database.
            state.ClaimedPrizeIds.Add(prizeId);

            logger.LogWarning(
                ex,
                "Duplicate claim of {TrackId}/{PrizeId} by player {PlayerId} was refused by the database.",
                trackId,
                prizeId,
                PlayerId
            );

            return RewardClaimOutcome.Fail(RewardClaimResult.AlreadyClaimed, trackId, prizeId);
        }

        state.ClaimedPrizeIds.Add(prizeId);

        await journal
            .TransitionAsync(
                operation,
                CommerceOperationState.Pivoted,
                CommerceStepKeys.REWARD_TRACK_GRANT,
                null,
                ct
            )
            .ConfigureAwait(true);

        RewardBundleResult granted = await rewards
            .GrantAsync(PlayerId, trackId, prizeId, prize.Rewards, operation, ct)
            .ConfigureAwait(true);

        await RecordGrantSummaryAsync(trackId, prizeId, granted.Summary, ct).ConfigureAwait(true);

        RewardTrackPrizeClaimedEvent claimed = new(PlayerId, trackId, prizeId, prize.Premium);

        if (granted.Complete)
        {
            // Into the journal first, then published. The journal holds the event, so a subscriber
            // that throws delays it rather than losing it — the relay sweep picks it up. Publishing
            // inside the claim instead would let a failing listener look like a failed claim.
            await journal.CompleteWithRelayAsync(operation, claimed, ct).ConfigureAwait(true);

            try
            {
                await events.PublishAsync(claimed, ct).ConfigureAwait(true);
                await journal.MarkRelayedAsync(operation, ct).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Publishing the claim of {TrackId}/{PrizeId} failed; the relay will retry it.",
                    trackId,
                    prizeId
                );
            }
        }

        logger.LogInformation(
            "Player {PlayerId} claimed {TrackId}/{PrizeId} at {Points} point(s): {Granted} reward(s) granted, {Failed} owed [{Summary}].",
            PlayerId,
            trackId,
            prizeId,
            state.Points,
            granted.Granted,
            granted.Failed,
            granted.Summary
        );

        await CheckCompletionAsync(track, state, now, ct).ConfigureAwait(true);

        // Reported as a success even when part of the bundle is owed. The claim happened, the
        // milestone is spent, and the operator's queue -- not the player's button -- is where an
        // undelivered reward belongs.
        return new RewardClaimOutcome
        {
            Result = RewardClaimResult.Success,
            TrackId = trackId,
            PrizeId = prizeId,
        };
    }

    /// <summary>
    /// Writes what was actually handed over onto the claim row. Best effort: the rewards are
    /// already with the player, and losing the note is not worth failing the claim over.
    /// </summary>
    private async Task RecordGrantSummaryAsync(
        string trackId,
        string prizeId,
        string summary,
        CancellationToken ct
    )
    {
        if (summary.Length == 0)
        {
            return;
        }

        try
        {
            await using VortexDbContext db = await dbCtxFactory
                .CreateDbContextAsync(ct)
                .ConfigureAwait(true);

            await db
                .PlayerRewardTrackClaims.Where(c =>
                    c.PlayerEntityId == PlayerId && c.TrackId == trackId && c.PrizeId == prizeId
                )
                .ExecuteUpdateAsync(up => up.SetProperty(c => c.GrantedSummary, summary), ct)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Could not record what {TrackId}/{PrizeId} granted to player {PlayerId}.",
                trackId,
                prizeId,
                PlayerId
            );
        }
    }

    private static RewardTrackPrizeDefinitionSnapshot? FindPrize(
        RewardTrackDefinitionSnapshot track,
        string prizeId
    )
    {
        foreach (RewardTrackPrizeDefinitionSnapshot prize in track.Prizes)
        {
            if (string.Equals(prize.PrizeId, prizeId, StringComparison.Ordinal))
            {
                return prize;
            }
        }

        return null;
    }
}
