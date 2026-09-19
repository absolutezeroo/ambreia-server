using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Dashboard.API.Operations.Safety.Contracts;
using Vortex.Observability.Diagnostics;
using Vortex.Primitives.Action;
using Vortex.Primitives.Catalog.Snapshots;
using Vortex.Primitives.Moderation;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Snapshots.Avatars;

namespace Vortex.Dashboard.API.Operations.Safety;

internal sealed class ModerationOperations(
    OperationRunner runner,
    ICfhTicketService cfhTickets,
    ISessionGateway sessionGateway,
    IGrainFactory grainFactory,
    StaffActorAccount staffActor
)
{
    private readonly OperationRunner _runner = runner;
    private readonly ICfhTicketService _cfhTickets = cfhTickets;
    private readonly ISessionGateway _sessionGateway = sessionGateway;
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly StaffActorAccount _staffActor = staffActor;

    public Task<OperationResult> KickPlayerAsync(
        KickPlayerRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.player.kick",
            actor,
            request.Reason,
            targetPlayerId: request.PlayerId,
            roomId: null,
            detail: new { },
            work: c =>
                _sessionGateway.RemoveSessionFromPlayerAsync(new PlayerId(request.PlayerId), c),
            ct
        );

    public Task<OperationResult> BanPlayerAsync(
        BanPlayerRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.player.ban",
            actor,
            request.Reason,
            targetPlayerId: request.PlayerId,
            roomId: null,
            detail: new { request.Permanent, request.DurationSeconds },
            work: async c =>
            {
                PlayerId staffActor = await _staffActor.PlayerIdAsync(c).ConfigureAwait(false);
                DateTime bannedUntil = request.Permanent
                    ? SanctionDuration.Permanent
                    : DateTime.UtcNow.AddSeconds(Math.Max(1, request.DurationSeconds ?? 0));

                bool ok = await _grainFactory
                    .GetPlayerGrain(new PlayerId(request.PlayerId))
                    .ApplyAccountBanAsync(staffActor, bannedUntil, request.Reason, c)
                    .ConfigureAwait(false);

                if (!ok)
                {
                    throw new InvalidOperationException("no_linked_account");
                }
            },
            ct,
            AuditCategory.Moderation
        );

    public Task<OperationResult> UnbanPlayerAsync(
        UnbanPlayerRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.player.unban",
            actor,
            request.Reason,
            targetPlayerId: request.PlayerId,
            roomId: null,
            detail: new { },
            work: async c =>
            {
                PlayerId staffActor = await _staffActor.PlayerIdAsync(c).ConfigureAwait(false);

                bool ok = await _grainFactory
                    .GetPlayerGrain(new PlayerId(request.PlayerId))
                    .ApplyAccountBanAsync(staffActor, bannedUntil: null, request.Reason, c)
                    .ConfigureAwait(false);

                if (!ok)
                {
                    throw new InvalidOperationException("no_linked_account");
                }
            },
            ct,
            AuditCategory.Moderation
        );

    public Task<OperationResult> TradingLockAsync(
        TradingLockRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.player.trading_lock",
            actor,
            request.Reason,
            targetPlayerId: request.PlayerId,
            roomId: null,
            detail: new { request.Permanent, request.DurationSeconds },
            work: async c =>
            {
                PlayerId staffActor = await _staffActor.PlayerIdAsync(c).ConfigureAwait(false);
                DateTime lockedUntil = request.Permanent
                    ? SanctionDuration.Permanent
                    : DateTime.UtcNow.AddSeconds(Math.Max(1, request.DurationSeconds ?? 0));

                bool ok = await _grainFactory
                    .GetPlayerGrain(new PlayerId(request.PlayerId))
                    .ApplyTradingLockAsync(staffActor, lockedUntil, c)
                    .ConfigureAwait(false);

                if (!ok)
                {
                    throw new InvalidOperationException("no_linked_account");
                }
            },
            ct,
            AuditCategory.Moderation
        );

    public Task<OperationResult> TradingUnlockAsync(
        TradingUnlockRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.player.trading_unlock",
            actor,
            request.Reason,
            targetPlayerId: request.PlayerId,
            roomId: null,
            detail: new { },
            work: async c =>
            {
                PlayerId staffActor = await _staffActor.PlayerIdAsync(c).ConfigureAwait(false);

                bool ok = await _grainFactory
                    .GetPlayerGrain(new PlayerId(request.PlayerId))
                    .ApplyTradingLockAsync(staffActor, lockedUntil: null, c)
                    .ConfigureAwait(false);

                if (!ok)
                {
                    throw new InvalidOperationException("no_linked_account");
                }
            },
            ct,
            AuditCategory.Moderation
        );

    public async Task<OperationResult> MutePlayerAsync(
        MutePlayerRequest request,
        string actor,
        CancellationToken ct
    )
    {
        PlayerId target = new(request.PlayerId);
        RoomPointerSnapshot activeRoom = await _grainFactory
            .GetPlayerPresenceGrain(target)
            .GetActiveRoomAsync()
            .ConfigureAwait(false);

        return await _runner
            .ExecuteAsync(
                "ops.player.mute",
                actor,
                request.Reason,
                targetPlayerId: request.PlayerId,
                roomId: activeRoom.RoomId > 0 ? activeRoom.RoomId.Value : null,
                detail: new { request.DurationSeconds },
                work: async c =>
                {
                    // Room-scoped mute is the only mute primitive in this codebase — there is no
                    // account-wide chat mute. This rejects up front (before the grain call) rather
                    // than letting MuteUserAsync silently no-op for a roomless target.
                    if (activeRoom.RoomId <= 0)
                    {
                        throw new InvalidOperationException("target_not_in_room");
                    }

                    PlayerId staffActor = await _staffActor.PlayerIdAsync(c).ConfigureAwait(false);
                    ActionContext actorCtx = ActionContext.CreateForPlayer(
                        staffActor,
                        activeRoom.RoomId
                    );

                    bool ok = await _grainFactory
                        .GetRoomModeration(activeRoom.RoomId)
                        .MuteUserAsync(actorCtx, target, request.DurationSeconds, c)
                        .ConfigureAwait(false);

                    if (!ok)
                    {
                        throw new InvalidOperationException("mute_rejected");
                    }
                },
                ct,
                AuditCategory.Moderation
            )
            .ConfigureAwait(false);
    }

    public Task<ImmutableArray<CfhIssueQueueEntrySnapshot>> GetCfhQueueAsync(
        CancellationToken ct
    ) => _cfhTickets.GetOpenQueueAsync(ct);

    public Task<ImmutableArray<CfhAppealSnapshot>> GetCfhAppealsAsync(CancellationToken ct) =>
        _cfhTickets.GetPendingAppealsAsync(ct);

    /// <remarks>
    /// Straight to the ticket service rather than through the moderation queue grain: an appeal
    /// resolution leaves the ticket closed and the in-client mod tool has no appeal screen to keep
    /// in sync, so there is no queue state for the grain to arbitrate.
    /// </remarks>
    public Task<OperationResult> ResolveCfhAppealsAsync(
        ResolveCfhAppealsRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.cfh.appeal.resolve",
            actor,
            "cfh appeal resolve",
            targetPlayerId: null,
            roomId: null,
            detail: new { request.IssueIds, request.Upheld },
            work: async c =>
            {
                await _cfhTickets
                    .ResolveAppealsAsync(request.IssueIds, request.Upheld, c)
                    .ConfigureAwait(false);
            },
            ct,
            AuditCategory.Moderation
        );

    public Task<OperationResult> PickCfhTicketsAsync(
        PickCfhTicketsRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.cfh.pick",
            actor,
            "cfh pick",
            targetPlayerId: null,
            roomId: null,
            detail: new { request.IssueIds },
            work: async c =>
            {
                PlayerId staffActor = await _staffActor.PlayerIdAsync(c).ConfigureAwait(false);

                // Same queue grain the in-client mod tool goes through: an operator picking here
                // and a moderator picking in the client are two interfaces onto one queue, and
                // they have to contend with each other rather than overwrite each other.
                await _grainFactory
                    .GetModerationQueueGrain()
                    .PickAsync(staffActor, request.IssueIds, retryEnabled: false, retryCount: 0, c)
                    .ConfigureAwait(false);
            },
            ct,
            AuditCategory.Moderation
        );

    public Task<OperationResult> CloseCfhTicketsAsync(
        CloseCfhTicketsRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.cfh.close",
            actor,
            "cfh close",
            targetPlayerId: null,
            roomId: null,
            detail: new
            {
                request.IssueIds,
                request.Reason,
                request.Sanctioned,
            },
            work: async c =>
            {
                PlayerId staffActor = await _staffActor.PlayerIdAsync(c).ConfigureAwait(false);

                await _grainFactory
                    .GetModerationQueueGrain()
                    .CloseAsync(
                        staffActor,
                        request.IssueIds,
                        (CfhTicketCloseReason)request.Reason,
                        request.Sanctioned,
                        c
                    )
                    .ConfigureAwait(false);
            },
            ct,
            AuditCategory.Moderation
        );

    public Task<OperationResult> ReleaseCfhTicketsAsync(
        ReleaseCfhTicketsRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.cfh.release",
            actor,
            "cfh release",
            targetPlayerId: null,
            roomId: null,
            detail: new { request.IssueIds },
            work: async c =>
            {
                PlayerId staffActor = await _staffActor.PlayerIdAsync(c).ConfigureAwait(false);

                await _grainFactory
                    .GetModerationQueueGrain()
                    .ReleaseAsync(staffActor, request.IssueIds, c)
                    .ConfigureAwait(false);
            },
            ct,
            AuditCategory.Moderation
        );
}
