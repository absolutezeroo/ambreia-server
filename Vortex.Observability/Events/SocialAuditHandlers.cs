using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Observability;

namespace Vortex.Observability.Events;

/// <summary>Translates social-graph domain events into durable audit records.</summary>
public sealed class FriendRequestAcceptedAuditHandler(IAuditSink audit)
    : IEventHandler<FriendRequestAcceptedEvent>
{
    public ValueTask HandleAsync(
        FriendRequestAcceptedEvent e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Social,
                Action = "social.friend_request_accepted",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.ActorPlayerId,
                TargetPlayerId = e.TargetPlayerId,
            }
        );

        return ValueTask.CompletedTask;
    }
}

public sealed class FriendRemovedAuditHandler(IAuditSink audit) : IEventHandler<FriendRemovedEvent>
{
    public ValueTask HandleAsync(FriendRemovedEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Social,
                Action = "social.friend_removed",
                Severity = AuditSeverity.Notice,
                Result = AuditResult.Success,
                ActorPlayerId = e.ActorPlayerId,
                TargetPlayerId = e.TargetPlayerId,
            }
        );

        return ValueTask.CompletedTask;
    }
}

public sealed class UserBlockedAuditHandler(IAuditSink audit) : IEventHandler<UserBlockedEvent>
{
    public ValueTask HandleAsync(UserBlockedEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Social,
                Action = "social.user_blocked",
                Severity = AuditSeverity.Notice,
                Result = AuditResult.Success,
                ActorPlayerId = e.ActorPlayerId,
                TargetPlayerId = e.TargetPlayerId,
            }
        );

        return ValueTask.CompletedTask;
    }
}

public sealed class UserUnblockedAuditHandler(IAuditSink audit) : IEventHandler<UserUnblockedEvent>
{
    public ValueTask HandleAsync(UserUnblockedEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Social,
                Action = "social.user_unblocked",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.ActorPlayerId,
                TargetPlayerId = e.TargetPlayerId,
            }
        );

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Respect is the one social currency a player spends on someone else, and it was the only such
/// exchange leaving no trace. Both halves are recorded: the giver's line answers "who did they
/// boost", the receiver's carries the running total a sudden jump shows up in.
/// </summary>
public sealed class RespectGivenAuditHandler(IAuditSink audit) : IEventHandler<RespectGivenEvent>
{
    public ValueTask HandleAsync(RespectGivenEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Social,
                Action = "social.respect_given",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.ActorPlayerId,
                TargetPlayerId = e.TargetPlayerId,
            }
        );

        return ValueTask.CompletedTask;
    }
}

public sealed class RespectReceivedAuditHandler(IAuditSink audit)
    : IEventHandler<RespectReceivedEvent>
{
    public ValueTask HandleAsync(RespectReceivedEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Social,
                Action = "social.respect_received",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.PlayerId,
                Data = JsonSerializer.Serialize(new { total = e.RespectTotal }),
            }
        );

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// The public half of an account's identity. Cheap to change, and the first thing changed when
/// someone is impersonating a player or covering their tracks, so the old value is kept in the
/// record rather than only the new one.
/// </summary>
public sealed class PlayerFigureChangedAuditHandler(IAuditSink audit)
    : IEventHandler<PlayerFigureChangedEvent>
{
    public ValueTask HandleAsync(PlayerFigureChangedEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Social,
                Action = "profile.figure_changed",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.PlayerId,
                Data = JsonSerializer.Serialize(new { figure = e.Figure }),
            }
        );

        return ValueTask.CompletedTask;
    }
}

public sealed class PlayerMottoChangedAuditHandler(IAuditSink audit)
    : IEventHandler<PlayerMottoChangedEvent>
{
    public ValueTask HandleAsync(PlayerMottoChangedEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Social,
                Action = "profile.motto_changed",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.PlayerId,
                Data = JsonSerializer.Serialize(new { motto = e.Motto }),
            }
        );

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// A wardrobe slot was saved. Filed next to the figure change it usually precedes: an account that
/// swaps look to something it had prepared earlier looks like two unrelated events otherwise.
/// </summary>
public sealed class WardrobeOutfitSavedAuditHandler(IAuditSink audit)
    : IEventHandler<WardrobeOutfitSavedEvent>
{
    public ValueTask HandleAsync(WardrobeOutfitSavedEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Social,
                Action = "profile.wardrobe_saved",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.PlayerId,
                Data = JsonSerializer.Serialize(new { e.SlotId, figure = e.Figure }),
            }
        );

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// A client preference was saved. Cheap and frequent, so it is Info and carries only which pane
/// changed -- the value itself is readable from the account, and what an investigation wants is the
/// moment, not the number.
/// </summary>
public sealed class AccountPreferenceChangedAuditHandler(IAuditSink audit)
    : IEventHandler<AccountPreferenceChangedEvent>
{
    public ValueTask HandleAsync(
        AccountPreferenceChangedEvent e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Social,
                Action = $"profile.preference_{e.Setting}",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.PlayerId,
            }
        );

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// A clothing item was redeemed. Filed under Item rather than the profile: a piece of furniture was
/// destroyed to produce it, and that is the half worth being able to follow.
/// </summary>
public sealed class ClothingRedeemedAuditHandler(IAuditSink audit)
    : IEventHandler<ClothingRedeemedEvent>
{
    public ValueTask HandleAsync(ClothingRedeemedEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Item,
                Action = "item.clothing_redeemed",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.PlayerId,
                Data = JsonSerializer.Serialize(new { definitionId = e.ItemId }),
            }
        );

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// A friend request was sent. The friendship itself was already recorded on acceptance; this is the
/// half that survives a refusal, which is the half a harassment report is made of.
/// </summary>
public sealed class FriendRequestSentAuditHandler(IAuditSink audit)
    : IEventHandler<FriendRequestSentEvent>
{
    public ValueTask HandleAsync(FriendRequestSentEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Social,
                Action = "social.friend_request_sent",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.ActorPlayerId,
                TargetPlayerId = e.TargetPlayerId,
            }
        );

        return ValueTask.CompletedTask;
    }
}

public sealed class FriendRequestDeclinedAuditHandler(IAuditSink audit)
    : IEventHandler<FriendRequestDeclinedEvent>
{
    public ValueTask HandleAsync(
        FriendRequestDeclinedEvent e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Social,
                // "Decline all" names nobody, so it gets its own action rather than a record with a
                // missing target that reads like data was lost.
                Action = e.RequesterPlayerId is null
                    ? "social.friend_request_declined_all"
                    : "social.friend_request_declined",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.ActorPlayerId,
                TargetPlayerId = e.RequesterPlayerId,
            }
        );

        return ValueTask.CompletedTask;
    }
}
