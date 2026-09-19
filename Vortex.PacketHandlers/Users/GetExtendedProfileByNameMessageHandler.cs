using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Badges.Snapshots;
using Vortex.Primitives.Groups.Snapshots;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Grains;
using Vortex.Protocol.Messages.Incoming.Users;
using Vortex.Protocol.Messages.Outgoing.Users;

namespace Vortex.PacketHandlers.Users;

public class GetExtendedProfileByNameMessageHandler
    : IMessageHandler<GetExtendedProfileByNameMessage>
{
    private readonly IGrainFactory _grainFactory;

    public GetExtendedProfileByNameMessageHandler(IGrainFactory grainFactory)
    {
        _grainFactory = grainFactory;
    }

    public async ValueTask HandleAsync(
        GetExtendedProfileByNameMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        IPlayerDirectoryGrain directoryGrain = _grainFactory.GetPlayerDirectoryGrain();
        PlayerId? playerId = await directoryGrain
            .GetPlayerIdAsync(message.UserName, ct)
            .ConfigureAwait(false);

        if (playerId is null)
        {
            return;
        }

        PlayerExtendedProfileSnapshot snapshot = await _grainFactory
            .GetPlayerGrain(playerId.Value)
            .GetExtendedProfileSnapshotAsync(ctx.PlayerId, ct)
            .ConfigureAwait(false);

        // Guild membership lives in the group directory, not on the player grain, so the profile's
        // guild list is assembled here rather than shipped empty.
        List<GuildInfoSnapshot> guilds = await _grainFactory
            .GetGroupDirectoryGrain()
            .GetMembershipsAsync(playerId.Value, ct)
            .ConfigureAwait(false);

        ImmutableArray<BadgeRarityCountSnapshot> rarities = await _grainFactory
            .GetPlayerBadgeGrain(playerId.Value)
            .GetRarityCountsAsync(ct)
            .ConfigureAwait(false);

        await ctx.SendComposerAsync(
                new ExtendedProfileMessageComposer
                {
                    UserId = (int)snapshot.UserId,
                    UserName = snapshot.UserName,
                    Figure = snapshot.Figure,
                    Motto = snapshot.Motto,
                    CreationDate = snapshot.CreationDate,
                    AchievementScore = snapshot.AchievementScore,
                    FriendCount = snapshot.FriendCount,
                    IsFriend = snapshot.IsFriend,
                    IsFriendRequestSent = snapshot.IsFriendRequestSent,
                    // Hidden wins over online: the client's three icons come off this one byte,
                    // and a hidden player who also reads as online would show the wrong one.
                    OnlineStatus =
                        snapshot.IsHidden ? OnlineStatusCodes.Hidden
                        : snapshot.IsOnline ? OnlineStatusCodes.Online
                        : OnlineStatusCodes.Offline,
                    Guilds = guilds,
                    LastAccessSinceInSeconds = snapshot.LastAccessSinceInSeconds,
                    OpenProfileWindow = snapshot.OpenProfileWindow,
                    IsHidden = snapshot.IsHidden,
                    AccountLevel = snapshot.AccountLevel,
                    IntegerField24 = snapshot.IntegerField24,
                    StarGemCount = snapshot.StarGemCount,
                    BooleanField26 = snapshot.BooleanField26,
                    BooleanField27 = snapshot.BooleanField27,
                    // The rank stays -1, the client's own "unranked" value — it hides the row below
                    // zero and would otherwise print "(#0)" on every profile
                    // (groups/ExtendedProfileWindowCtrl.as:318). Filling it means ranking the
                    // player against the whole hotel, which is the badge leaderboard's job.
                    TotalBadges = snapshot.TotalBadges,
                    AchievementLevel = snapshot.AchievementLevel,
                    BadgeRarityCounts =
                    [
                        .. rarities.Select(r => new BadgeRarityCount
                        {
                            RarityId = (int)r.Rarity,
                            Count = r.Count,
                        }),
                    ],
                    TotalBadgesRank = -1,
                },
                ct
            )
            .ConfigureAwait(false);
    }
}
