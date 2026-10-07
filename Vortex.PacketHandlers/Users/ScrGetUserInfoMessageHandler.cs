using System;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.PacketHandlers.Preferences;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Players.Grains;
using Vortex.Protocol.Messages.Incoming.Users;
using Vortex.Protocol.Messages.Outgoing.Users;

namespace Vortex.PacketHandlers.Users;

public class ScrGetUserInfoMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<ScrGetUserInfoMessage>
{
    private readonly IGrainFactory _grainFactory = grainFactory;

    public async ValueTask HandleAsync(
        ScrGetUserInfoMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0)
        {
            return;
        }

        IPlayerGrain player = _grainFactory.GetPlayerGrain(ctx.PlayerId);

        MembershipSnapshot sub = await player.GetMembershipAsync(ct).ConfigureAwait(false);

        await ctx.SendComposerAsync(BuildScrSendUserInfo(message.ProductName, sub), ct)
            .ConfigureAwait(false);

        await ctx.SendComposerAsync(
                await AccountPreferences.BuildAsync(player, ct).ConfigureAwait(false),
                ct
            )
            .ConfigureAwait(false);
    }

    private static ScrSendUserInfoMessageComposer BuildScrSendUserInfo(
        string productName,
        MembershipSnapshot sub
    )
    {
        int daysLeft = sub.DaysLeft;
        int rem = daysLeft % 31;
        return new ScrSendUserInfoMessageComposer
        {
            ProductName = productName,
            DaysToPeriodEnd = sub.IsActive ? (rem == 0 ? 31 : rem) : 0,
            MemberPeriods = sub.TotalMonths,
            PeriodsSubscribedAhead = sub.IsActive ? daysLeft / 31 - (rem == 0 ? 1 : 0) : 0,
            ResponseType = sub.IsActive ? 2 : 0,
            HasEverBeenMember = sub.TotalMonths > 0 || sub.IsActive,
            IsVIP = sub.IsActive,
            PastMembershipDays = sub.PastMembershipDays,
            PastVipDays = sub.PastVipDays,
            MinutesUntilExpiration = sub.IsActive
                ? (int)(sub.ExpiresAt - DateTime.UtcNow).TotalMinutes
                : 0,
            MinutesSinceLastModified = 0,
        };
    }
}
