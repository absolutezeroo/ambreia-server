using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Observability;

namespace Vortex.Observability.Events;

/// <summary>
/// Turns player progression into durable audit records. Progression was the one half of a player's
/// life the forensics timeline could not see at all: an investigation could follow the furni and the
/// credits, but not what the account actually earned along the way.
/// </summary>
public sealed class AchievementLevelUpAuditHandler(IAuditSink audit)
    : IEventHandler<AchievementLevelUpEvent>
{
    public ValueTask HandleAsync(AchievementLevelUpEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Progression,
                Action = "achievement.level_up",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.PlayerId,
                Data = JsonSerializer.Serialize(
                    new
                    {
                        achievement = e.AchievementName,
                        e.Level,
                        badge = e.BadgeCode,
                        points = e.ScoreGained,
                    }
                ),
            }
        );

        return ValueTask.CompletedTask;
    }
}

/// <summary>A badge entered the player's collection. Re-grants never reach here.</summary>
public sealed class BadgeGrantedAuditHandler(IAuditSink audit) : IEventHandler<BadgeGrantedEvent>
{
    public ValueTask HandleAsync(BadgeGrantedEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Progression,
                Action = "badge.granted",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.PlayerId,
                Data = JsonSerializer.Serialize(new { badge = e.BadgeCode }),
            }
        );

        return ValueTask.CompletedTask;
    }
}

/// <summary>The player changed which badges they wear.</summary>
public sealed class BadgesEquippedAuditHandler(IAuditSink audit)
    : IEventHandler<BadgesEquippedEvent>
{
    public ValueTask HandleAsync(BadgesEquippedEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Progression,
                Action = "badge.equipped",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.PlayerId,
                Data = JsonSerializer.Serialize(
                    new { badges = string.Join(", ", e.BadgeCodes), count = e.BadgeCodes.Length }
                ),
            }
        );

        return ValueTask.CompletedTask;
    }
}

/// <summary>A quest was finished and its reward paid.</summary>
public sealed class QuestCompletedAuditHandler(IAuditSink audit)
    : IEventHandler<QuestCompletedEvent>
{
    public ValueTask HandleAsync(QuestCompletedEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Progression,
                Action = "quest.completed",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.PlayerId,
                Data = JsonSerializer.Serialize(
                    new
                    {
                        quest = e.LocalizationCode,
                        campaign = e.CampaignCode,
                        e.QuestId,
                        rewardType = e.RewardType,
                        reward = e.RewardAmount,
                    }
                ),
            }
        );

        return ValueTask.CompletedTask;
    }
}

/// <summary>A quest became the player's active one.</summary>
public sealed class QuestAcceptedAuditHandler(IAuditSink audit) : IEventHandler<QuestAcceptedEvent>
{
    public ValueTask HandleAsync(QuestAcceptedEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Progression,
                Action = "quest.accepted",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.PlayerId,
                Data = JsonSerializer.Serialize(
                    new
                    {
                        quest = e.LocalizationCode,
                        campaign = e.CampaignCode,
                        e.QuestId,
                    }
                ),
            }
        );

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// A quest was dropped. Turning one down and abandoning the one in progress reach the grain by the
/// same path, so the action string is what keeps them apart on the timeline.
/// </summary>
public sealed class QuestAbandonedAuditHandler(IAuditSink audit)
    : IEventHandler<QuestAbandonedEvent>
{
    public ValueTask HandleAsync(QuestAbandonedEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Progression,
                Action = e.Rejected ? "quest.rejected" : "quest.cancelled",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.PlayerId,
                Data = JsonSerializer.Serialize(
                    new
                    {
                        quest = e.LocalizationCode,
                        campaign = e.CampaignCode,
                        e.QuestId,
                    }
                ),
            }
        );

        return ValueTask.CompletedTask;
    }
}

/// <summary>An avatar effect was activated and started burning its duration.</summary>
public sealed class AvatarEffectActivatedAuditHandler(IAuditSink audit)
    : IEventHandler<AvatarEffectActivatedEvent>
{
    public ValueTask HandleAsync(
        AvatarEffectActivatedEvent e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Progression,
                Action = "effect.activated",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.PlayerId,
                Data = JsonSerializer.Serialize(new { e.EffectId, duration = e.DurationSeconds }),
            }
        );

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// A resolution challenge was won on a statue. The item id matters as much as the player: the
/// statue is furniture, and the badge it produced outlives whoever owns it next.
/// </summary>
public sealed class AchievementResolutionWonAuditHandler(IAuditSink audit)
    : IEventHandler<AchievementResolutionWonEvent>
{
    public ValueTask HandleAsync(
        AchievementResolutionWonEvent e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Progression,
                Action = "achievement.resolution_won",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.PlayerId,
                ItemId = e.ItemId,
                Data = JsonSerializer.Serialize(
                    new
                    {
                        e.AchievementId,
                        e.TargetLevel,
                        badge = e.BadgeCode,
                    }
                ),
            }
        );

        return ValueTask.CompletedTask;
    }
}

public sealed class AchievementResolutionResetAuditHandler(IAuditSink audit)
    : IEventHandler<AchievementResolutionResetEvent>
{
    public ValueTask HandleAsync(
        AchievementResolutionResetEvent e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Progression,
                Action = "achievement.resolution_reset",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.PlayerId,
                ItemId = e.ItemId,
            }
        );

        return ValueTask.CompletedTask;
    }
}

public sealed class DailyTaskClaimedAuditHandler(IAuditSink audit)
    : IEventHandler<DailyTaskClaimedEvent>
{
    public ValueTask HandleAsync(DailyTaskClaimedEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Progression,
                Action = "daily_task.claimed",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.PlayerId,
                Data = JsonSerializer.Serialize(new { e.TaskId }),
            }
        );

        return ValueTask.CompletedTask;
    }
}
