using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Dashboard.API.Operations.Progression.Contracts;
using Vortex.Primitives.Habbicons;
using Vortex.Primitives.Habbicons.Admin;
using Vortex.Primitives.RewardTracks;
using Vortex.Primitives.RewardTracks.Admin;

namespace Vortex.Dashboard.API.Operations.Progression;

/// <summary>
/// Habbicon and reward-track admin operations.
/// </summary>
/// <remarks>
/// Every one routes through <see cref="IHabbiconAdminService"/> or
/// <see cref="IRewardTrackAdminService"/> — never a direct database write. Those services reload the
/// in-process catalogs and, for reward tracks, tell the players who already have progress that the
/// content changed under them. A raw write here would be invisible to both.
/// </remarks>
internal sealed class RewardOperations(
    OperationRunner runner,
    IHabbiconAdminService habbiconAdmin,
    IRewardTrackAdminService rewardTrackAdmin,
    IRewardTrackCatalog rewardTrackCatalog
)
{
    private readonly OperationRunner _runner = runner;
    private readonly IHabbiconAdminService _habbiconAdmin = habbiconAdmin;
    private readonly IRewardTrackAdminService _rewardTrackAdmin = rewardTrackAdmin;
    private readonly IRewardTrackCatalog _rewardTrackCatalog = rewardTrackCatalog;

    // Habbicons -----------------------------------------------------------------------------

    public Task<OperationResult> CreateHabbiconCollectionAsync(
        CreateHabbiconCollectionRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.habbicon.collection.create",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.Code, request.CampaignCode },
            work: async c =>
                Throw(
                    await _habbiconAdmin
                        .CreateCollectionAsync(ToSpec(request), c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> UpdateHabbiconCollectionAsync(
        UpdateHabbiconCollectionRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.habbicon.collection.update",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.CollectionId, request.Code },
            work: async c =>
                Throw(
                    await _habbiconAdmin
                        .UpdateCollectionAsync(request.CollectionId, ToSpec(request), c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> DeleteHabbiconCollectionAsync(
        DeleteHabbiconCollectionRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.habbicon.collection.delete",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.CollectionId },
            work: async c =>
                Throw(
                    await _habbiconAdmin
                        .DeleteCollectionAsync(request.CollectionId, c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> CreateHabbiconAsync(
        CreateHabbiconRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.habbicon.create",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new
            {
                request.Code,
                request.CollectionId,
                request.IsCollectionReward,
            },
            work: async c =>
                Throw(
                    await _habbiconAdmin
                        .CreateHabbiconAsync(ToSpec(request), c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> UpdateHabbiconAsync(
        UpdateHabbiconRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.habbicon.update",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.HabbiconId, request.Code },
            work: async c =>
                Throw(
                    await _habbiconAdmin
                        .UpdateHabbiconAsync(request.HabbiconId, ToSpec(request), c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> DeleteHabbiconAsync(
        DeleteHabbiconRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.habbicon.delete",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.HabbiconId },
            work: async c =>
                Throw(
                    await _habbiconAdmin
                        .DeleteHabbiconAsync(request.HabbiconId, c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> GrantHabbiconAsync(
        GrantHabbiconRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.habbicon.grant",
            actor,
            request.Reason,
            targetPlayerId: request.PlayerId,
            roomId: null,
            detail: new { request.HabbiconId },
            work: async c =>
                Throw(
                    await _habbiconAdmin
                        .GrantAsync(request.PlayerId, request.HabbiconId, c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> RevokeHabbiconAsync(
        RevokeHabbiconRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.habbicon.revoke",
            actor,
            request.Reason,
            targetPlayerId: request.PlayerId,
            roomId: null,
            detail: new { request.HabbiconId },
            work: async c =>
                Throw(
                    await _habbiconAdmin
                        .RevokeAsync(request.PlayerId, request.HabbiconId, c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    // Reward tracks -------------------------------------------------------------------------

    public Task<OperationResult> CreateRewardTrackAsync(
        CreateRewardTrackRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.reward_track.create",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.TrackId, request.CampaignCode },
            work: async c =>
                Throw(
                    await _rewardTrackAdmin
                        .CreateTrackAsync(ToSpec(request), c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> UpdateRewardTrackAsync(
        UpdateRewardTrackRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.reward_track.update",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.TrackRowId, request.TrackId },
            work: async c =>
                Throw(
                    await _rewardTrackAdmin
                        .UpdateTrackAsync(request.TrackRowId, ToSpec(request), c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> CloneRewardTrackAsync(
        CloneRewardTrackRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.reward_track.clone",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.TrackRowId, request.NewTrackId },
            work: async c =>
                Throw(
                    await _rewardTrackAdmin
                        .CloneTrackAsync(request.TrackRowId, request.NewTrackId, c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> PublishRewardTrackAsync(
        RewardTrackRowRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.reward_track.publish",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.TrackRowId },
            // Refuses a track the content validator reports problems on. That refusal surfaces here
            // as content_invalid, which is the point: an unpublishable campaign is an afternoon's
            // editing, a published broken one is a support ticket per player.
            work: async c =>
                Throw(
                    await _rewardTrackAdmin
                        .PublishTrackAsync(request.TrackRowId, c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> ArchiveRewardTrackAsync(
        RewardTrackRowRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.reward_track.archive",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.TrackRowId },
            work: async c =>
                Throw(
                    await _rewardTrackAdmin
                        .ArchiveTrackAsync(request.TrackRowId, c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> DeleteRewardTrackAsync(
        RewardTrackRowRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.reward_track.delete",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.TrackRowId },
            work: async c =>
                Throw(
                    await _rewardTrackAdmin
                        .DeleteTrackAsync(request.TrackRowId, c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> UpsertRewardTrackTaskAsync(
        UpsertRewardTrackTaskRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.reward_track.task.upsert",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new
            {
                request.TrackRowId,
                request.TaskId,
                request.ActionCode,
                levels = request.Levels.Count,
                steps = request.Steps?.Count ?? 0,
            },
            work: async c =>
                Throw(
                    await _rewardTrackAdmin
                        .UpsertTaskAsync(
                            request.TrackRowId,
                            new RewardTrackTaskSpec(
                                request.TaskId,
                                request.ActionCode,
                                request.Parameter,
                                (TaskProgressMode)request.Mode,
                                request.Premium,
                                request.SortOrder,
                                [
                                    .. request.Levels.Select(l => new RewardTrackTaskLevelSpec(
                                        l.RequiredCount,
                                        l.PointsReward,
                                        l.Premium
                                    )),
                                ],
                                [
                                    .. (
                                        request.Steps ?? Array.Empty<RewardTrackTaskStepBody>()
                                    ).Select(s => new RewardTrackTaskStepSpec(
                                        s.ActionCode,
                                        [
                                            .. (
                                                s.Filters
                                                ?? Array.Empty<RewardTrackStepFilterBody>()
                                            ).Select(Filter),
                                        ],
                                        Nodes(s.Tree)
                                    )),
                                ]
                            ),
                            c
                        )
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> DeleteRewardTrackTaskAsync(
        DeleteRewardTrackTaskRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.reward_track.task.delete",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.TaskRowId },
            work: async c =>
                Throw(
                    await _rewardTrackAdmin
                        .DeleteTaskAsync(request.TaskRowId, c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> UpsertRewardTrackPrizeAsync(
        UpsertRewardTrackPrizeRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.reward_track.prize.upsert",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new
            {
                request.TrackRowId,
                request.PrizeId,
                request.RequiredPoints,
                rewards = request.Rewards.Count,
            },
            work: async c =>
                Throw(
                    await _rewardTrackAdmin
                        .UpsertPrizeAsync(
                            request.TrackRowId,
                            new RewardTrackPrizeSpec(
                                request.PrizeId,
                                request.RequiredPoints,
                                request.Premium,
                                request.SortOrder,
                                [
                                    .. request.Rewards.Select(r => new RewardTrackRewardSpec(
                                        (RewardKind)r.Kind,
                                        r.RewardTypeId,
                                        r.Amount,
                                        r.ExtraParams,
                                        r.SortOrder
                                    )),
                                ]
                            ),
                            c
                        )
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> DeleteRewardTrackPrizeAsync(
        DeleteRewardTrackPrizeRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.reward_track.prize.delete",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.PrizeRowId },
            work: async c =>
                Throw(
                    await _rewardTrackAdmin
                        .DeletePrizeAsync(request.PrizeRowId, c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> ResetPlayerRewardTrackAsync(
        ResetPlayerRewardTrackRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.reward_track.player.reset",
            actor,
            request.Reason,
            targetPlayerId: request.PlayerId,
            roomId: null,
            detail: new { request.TrackId },
            work: async c =>
                Throw(
                    await _rewardTrackAdmin
                        .ResetPlayerTrackAsync(request.PlayerId, request.TrackId, c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> GrantRewardTrackPremiumAsync(
        GrantRewardTrackPremiumRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.reward_track.player.grant_premium",
            actor,
            request.Reason,
            targetPlayerId: request.PlayerId,
            roomId: null,
            detail: new { request.TrackId },
            work: async c =>
                Throw(
                    await _rewardTrackAdmin
                        .GrantPremiumAsync(request.PlayerId, request.TrackId, c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    /// <summary>
    /// Turns an admin service's refusal into the exception <see cref="ExecuteAsync"/> records and
    /// reports. The services answer with a result rather than throwing because they have callers
    /// other than this one; this is where a refusal becomes an operator-visible failure.
    /// </summary>
    private static void Throw(HabbiconAdminResult result)
    {
        if (!result.Success)
        {
            throw new InvalidOperationException(result.ErrorCode);
        }
    }

    private static void Throw(RewardTrackAdminResult result)
    {
        if (!result.Success)
        {
            throw new InvalidOperationException(result.ErrorCode);
        }
    }

    private static HabbiconCollectionSpec ToSpec(CreateHabbiconCollectionRequest r) =>
        new(
            r.Code,
            r.SortOrder,
            r.Enabled,
            r.Hidden,
            r.AvailableFrom,
            r.AvailableUntil,
            r.PriceCredits,
            r.PriceActivityPoints,
            r.ActivityPointType,
            r.CampaignCode
        );

    private static HabbiconCollectionSpec ToSpec(UpdateHabbiconCollectionRequest r) =>
        new(
            r.Code,
            r.SortOrder,
            r.Enabled,
            r.Hidden,
            r.AvailableFrom,
            r.AvailableUntil,
            r.PriceCredits,
            r.PriceActivityPoints,
            r.ActivityPointType,
            r.CampaignCode
        );

    private static HabbiconSpec ToSpec(CreateHabbiconRequest r) =>
        new(
            r.Code,
            r.CollectionId,
            r.SortOrder,
            r.IsCollectionReward,
            r.PriceCredits,
            r.PriceActivityPoints,
            r.ActivityPointType,
            r.Enabled,
            r.AvailableFrom,
            r.AvailableUntil
        );

    private static HabbiconSpec ToSpec(UpdateHabbiconRequest r) =>
        new(
            r.Code,
            r.CollectionId,
            r.SortOrder,
            r.IsCollectionReward,
            r.PriceCredits,
            r.PriceActivityPoints,
            r.ActivityPointType,
            r.Enabled,
            r.AvailableFrom,
            r.AvailableUntil
        );

    private static RewardTrackSpec ToSpec(CreateRewardTrackRequest r) =>
        new(
            r.TrackId,
            r.Theme,
            // A create is always a draft whatever is passed; the admin service enforces it too.
            // Publishing is the operation that validates.
            RewardTrackStatus.Draft,
            r.SortOrder,
            r.StartsAt,
            r.ProgressEndsAt,
            r.ClaimEndsAt,
            (RewardTrackUnlockKind)r.UnlockKind,
            r.UnlockValue,
            (RewardTrackCompletionPolicy)r.CompletionPolicy,
            r.PremiumEnabled,
            r.PremiumBoostPerMille,
            r.PremiumInstantPoints,
            r.PremiumCostCredits,
            r.PremiumCostDiamonds,
            r.Hidden,
            r.CampaignCode
        );

    private static RewardTrackStepFilterSpec Filter(RewardTrackStepFilterBody f) =>
        new(f.FactKey, (StepFilterOperator)f.Op, f.Value, Side(f.Left), Side(f.Right));

    /// <summary>
    /// One side, or <c>null</c> when the caller named none.
    /// </summary>
    /// <remarks>
    /// Nothing is checked here beyond the shape of the JSON — a kind outside the enum, a step that
    /// points forward, a fact the action never reports. All of it reaches
    /// <c>RewardTrackSequenceRules</c>, which is where every other refusal lives and the only place
    /// that answers with a code the editor knows how to show.
    /// </remarks>
    private static RewardTrackStepValueSpec? Side(RewardTrackStepValueBody? value) =>
        value is null
            ? null
            : new RewardTrackStepValueSpec((StepValueKind)value.Kind, value.Arg, value.Step);

    /// <summary>
    /// A step's condition tree as the spec spells it, or <c>null</c> when the caller sent none.
    /// </summary>
    /// <remarks>
    /// Nothing is checked here beyond the shape of the JSON: a kind outside the enum, a group with
    /// no children, a test with children — all of it is carried through to
    /// <c>RewardTrackSequenceRules</c>, which is where every other refusal already lives and the
    /// only place that can answer with a code the editor knows how to show.
    /// </remarks>
    private static IReadOnlyList<RewardTrackStepNodeSpec>? Nodes(
        IReadOnlyList<RewardTrackStepNodeBody>? nodes
    ) =>
        nodes is null
            ? null
            :
            [
                .. nodes.Select(n => new RewardTrackStepNodeSpec(
                    (PredicateKind)n.Kind,
                    n.Test is null ? null : Filter(n.Test),
                    Nodes(n.Children)
                )),
            ];

    private RewardTrackSpec ToSpec(UpdateRewardTrackRequest r) =>
        new(
            r.TrackId,
            r.Theme,
            // The status is not on the update request. Read back from the catalog so an edit leaves
            // the lifecycle exactly where it was: a live campaign stays live, a draft stays a draft.
            _rewardTrackCatalog.TryGetTrack(
                r.TrackId,
                out Primitives.RewardTracks.Snapshots.RewardTrackDefinitionSnapshot? existing
            )
                ? existing.Status
                : RewardTrackStatus.Draft,
            r.SortOrder,
            r.StartsAt,
            r.ProgressEndsAt,
            r.ClaimEndsAt,
            (RewardTrackUnlockKind)r.UnlockKind,
            r.UnlockValue,
            (RewardTrackCompletionPolicy)r.CompletionPolicy,
            r.PremiumEnabled,
            r.PremiumBoostPerMille,
            r.PremiumInstantPoints,
            r.PremiumCostCredits,
            r.PremiumCostDiamonds,
            r.Hidden,
            r.CampaignCode
        );
}
