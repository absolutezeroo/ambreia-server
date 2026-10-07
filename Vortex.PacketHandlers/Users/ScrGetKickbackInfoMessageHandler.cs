using System;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Players;
using Vortex.Primitives.Server.Grains;
using Vortex.Protocol.Messages.Incoming.Users;
using Vortex.Protocol.Messages.Outgoing.Users;

namespace Vortex.PacketHandlers.Users;

public class ScrGetKickbackInfoMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<ScrGetKickbackInfoMessage>
{
    private readonly IGrainFactory _grainFactory = grainFactory;

    public async ValueTask HandleAsync(
        ScrGetKickbackInfoMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0)
        {
            return;
        }

        MembershipSnapshot sub = await _grainFactory
            .GetPlayerGrain(ctx.PlayerId)
            .GetMembershipAsync(ct)
            .ConfigureAwait(false);

        int kickbackPercent = await _grainFactory
            .GetServerConfigGrain()
            .GetIntAsync(
                MembershipConfigKeys.KickbackPercentKey,
                MembershipConfigKeys.KickbackPercentDefault
            )
            .ConfigureAwait(false);

        DateTime now = DateTime.UtcNow;
        int streakBonus = Math.Min(sub.TotalMonths, 31);
        int monthlyReward = (int)(sub.CreditsSpentThisPeriod * (kickbackPercent / 100.0));
        int minutesUntilPayday =
            sub.IsActive && sub.PaydayAt.HasValue && sub.PaydayAt.Value > now
                ? (int)(sub.PaydayAt.Value - now).TotalMinutes
                : 0;

        await ctx.SendComposerAsync(
                new ScrSendKickbackInfoMessageComposer
                {
                    CurrentHcStreak = sub.TotalMonths,
                    FirstSubscriptionDate =
                        sub.IsActive && sub.TotalMonths > 0
                            ? sub.ExpiresAt.AddMonths(-sub.TotalMonths).ToString("yyyy-MM-dd")
                            : string.Empty,
                    KickbackPercentage = sub.IsActive ? kickbackPercent / 100.0 : 0.0,
                    TotalCreditsMissed = 0,
                    TotalCreditsRewarded = sub.TotalCreditsRewarded,
                    TotalCreditsSpent = sub.TotalCreditsSpent,
                    CreditRewardForStreakBonus = sub.IsActive ? streakBonus : 0,
                    CreditRewardForMonthlySpent = sub.IsActive ? monthlyReward : 0,
                    TimeUntilPayday = minutesUntilPayday,
                },
                ct
            )
            .ConfigureAwait(false);
    }
}
