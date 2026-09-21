using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Notifications;
using Vortex.WebApi.Services;

namespace Vortex.WebApi.Notifications;

/// <summary>
/// What I earned.
/// </summary>
/// <remarks>
/// This is the loud theme. An active player levels an achievement several times an hour, and a
/// notification list that is nine tenths achievements is a list nobody reads — which costs the
/// quiet kinds (a club that lapsed, somebody who blocked you) their whole reason for existing.
///
/// <para>
/// Left in because it was asked for, and split across distinct kinds so that
/// <see cref="NotificationKind.AchievementLevelUp"/> can be dropped on its own. If the list turns
/// out to be unreadable, that is the one to cut, and cutting it is deleting this first handler.
/// </para>
/// </remarks>
public sealed class AchievementNotificationHandler(INotificationSink notifications)
    : IEventHandler<AchievementLevelUpEvent>
{
    public async ValueTask HandleAsync(
        AchievementLevelUpEvent e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        await notifications
            .NotifyAsync(
                e.PlayerId,
                NotificationKind.AchievementLevelUp,
                targetId: e.AchievementId,
                data: JsonSerializer.Serialize(
                    new
                    {
                        name = e.AchievementName,
                        level = e.Level,
                        badge = e.BadgeCode,
                    }
                ),
                ct: ct
            )
            .ConfigureAwait(false);
    }
}

/// <summary>A quest came out.</summary>
public sealed class QuestNotificationHandler(INotificationSink notifications)
    : IEventHandler<QuestCompletedEvent>
{
    public async ValueTask HandleAsync(
        QuestCompletedEvent e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        await notifications
            .NotifyAsync(
                e.PlayerId,
                NotificationKind.QuestCompleted,
                targetId: e.QuestId,
                data: JsonSerializer.Serialize(
                    new
                    {
                        campaign = e.CampaignCode,
                        code = e.LocalizationCode,
                        reward = e.RewardAmount,
                    }
                ),
                ct: ct
            )
            .ConfigureAwait(false);
    }
}

/// <summary>A badge landed in my collection.</summary>
public sealed class BadgeNotificationHandler(INotificationSink notifications)
    : IEventHandler<BadgeGrantedEvent>
{
    public async ValueTask HandleAsync(BadgeGrantedEvent e, EventContext ctx, CancellationToken ct)
    {
        await notifications
            .NotifyAsync(
                e.PlayerId,
                NotificationKind.BadgeGranted,
                data: JsonSerializer.Serialize(new { badge = e.BadgeCode }),
                ct: ct
            )
            .ConfigureAwait(false);
    }
}

/// <summary>A Habbicon collection filled up, and there is a reward waiting for it.</summary>
public sealed class HabbiconCollectionNotificationHandler(INotificationSink notifications)
    : IEventHandler<HabbiconCollectionCompletedEvent>
{
    public async ValueTask HandleAsync(
        HabbiconCollectionCompletedEvent e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        await notifications
            .NotifyAsync(
                e.PlayerId,
                NotificationKind.HabbiconCollectionCompleted,
                targetId: e.CollectionId,
                data: JsonSerializer.Serialize(
                    new { collection = e.CollectionCode, reward = e.RewardHabbiconId }
                ),
                ct: ct
            )
            .ConfigureAwait(false);
    }
}

/// <summary>A prize pool paid out to me.</summary>
public sealed class PrizeNotificationHandler(INotificationSink notifications)
    : IEventHandler<PrizeAwardedEvent>
{
    public async ValueTask HandleAsync(PrizeAwardedEvent e, EventContext ctx, CancellationToken ct)
    {
        await notifications
            .NotifyAsync(
                e.PlayerId,
                NotificationKind.PrizeAwarded,
                targetId: e.ClassId,
                data: JsonSerializer.Serialize(
                    new
                    {
                        pool = e.PoolCode,
                        variant = e.Variant,
                        source = e.Source,
                    }
                ),
                ct: ct
            )
            .ConfigureAwait(false);
    }
}
