using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Database.Commerce;
using Vortex.Database.Context;
using Vortex.Database.Entities.Commerce;
using Vortex.Database.Entities.Players;
using Vortex.Logging;
using Vortex.Players.Configuration;
using Vortex.Primitives;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Events;
using Vortex.Primitives.Inventory.Grains;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Players.Providers;
using Vortex.Primitives.Players.Wallet;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Server.Grains;
using Vortex.Protocol.Messages.Outgoing.Users;

namespace Vortex.Players.Grains;

internal sealed partial class PlayerGrain : Grain, IPlayerGrain
{
    private readonly ClubConfig _clubConfig;
    private readonly IDbContextFactory<VortexDbContext> _dbCtxFactory;
    private readonly IEventPublisher _events;
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger<PlayerGrain> _logger;
    private readonly IAccountLevelProvider _accountLevels;
    private readonly ICommerceJournal _journal;

    private readonly PlayerLiveState _state;

    public PlayerGrain(
        IDbContextFactory<VortexDbContext> dbCtxFactory,
        IGrainFactory grainFactory,
        IEventPublisher events,
        ILogger<PlayerGrain> logger,
        IOptions<ClubConfig> clubConfig,
        IAccountLevelProvider accountLevels,
        ICommerceJournal journal
    )
    {
        _dbCtxFactory = dbCtxFactory;
        _grainFactory = grainFactory;
        _events = events;
        _logger = logger;
        _clubConfig = clubConfig.Value;
        _accountLevels = accountLevels;
        _journal = journal;

        _state = new PlayerLiveState { PlayerId = PlayerId.Parse((int)this.GetPrimaryKeyLong()) };
    }

    public async Task SetFigureAsync(string figure, AvatarGenderType gender, CancellationToken ct)
    {
        _state.Figure = figure;
        _state.Gender = gender;

        await WriteToDatabaseAsync(ct);

        IPlayerPresenceGrain playerPresence = _grainFactory.GetPlayerPresenceGrain(
            (int)this.GetPrimaryKeyLong()
        );

        await playerPresence.OnFigureUpdatedAsync(await GetSummaryAsync(ct), ct);

        await _events
            .PublishAsync(new PlayerFigureChangedEvent(_state.PlayerId, figure), ct)
            .ConfigureAwait(true);
    }

    public async Task<bool> TryGiveRespectAsync(CancellationToken ct)
    {
        int dailyLimit = await this
            .GrainFactory.GetServerConfigGrain()
            .GetIntAsync(RespectConfig.DailyLimitKey, RespectConfig.DailyLimitDefault)
            .ConfigureAwait(true);

        (bool allowed, int givenToday, DateTime resetDate) = RespectBudget.TryConsume(
            _state.RespectGivenToday,
            _state.RespectResetDate,
            DateTime.UtcNow,
            dailyLimit
        );

        // The reset date is rolled over whether or not the respect is allowed: a refused give on a
        // new day still means today's budget is the one being counted from now on.
        RollRespectDayIfNeeded(resetDate);

        _state.RespectGivenToday = givenToday;

        if (!allowed)
        {
            return false;
        }

        await WriteToDatabaseAsync(ct);

        return true;
    }

    public async Task<bool> TryGivePetRespectAsync(CancellationToken ct)
    {
        int dailyLimit = await this
            .GrainFactory.GetServerConfigGrain()
            .GetIntAsync(RespectConfig.PetDailyLimitKey, RespectConfig.PetDailyLimitDefault)
            .ConfigureAwait(true);

        (bool allowed, int givenToday, DateTime resetDate) = RespectBudget.TryConsume(
            _state.PetRespectGivenToday,
            _state.RespectResetDate,
            DateTime.UtcNow,
            dailyLimit
        );

        RollRespectDayIfNeeded(resetDate);

        _state.PetRespectGivenToday = givenToday;

        if (!allowed)
        {
            return false;
        }

        await WriteToDatabaseAsync(ct);

        return true;
    }

    public async Task<bool> ReplenishRespectAsync(CancellationToken ct)
    {
        int allowance = await this
            .GrainFactory.GetServerConfigGrain()
            .GetIntAsync(RespectConfig.DailyReplenishesKey, RespectConfig.DailyReplenishesDefault)
            .ConfigureAwait(true);

        (bool allowed, int usedToday, DateTime resetDate) = RespectBudget.TryConsume(
            _state.RespectReplenishesUsedToday,
            _state.RespectResetDate,
            DateTime.UtcNow,
            allowance
        );

        RollRespectDayIfNeeded(resetDate);

        if (!allowed)
        {
            return false;
        }

        _state.RespectReplenishesUsedToday = usedToday;
        _state.RespectGivenToday = 0;

        await WriteToDatabaseAsync(ct);

        return true;
    }

    public async Task<PlayerRespectSnapshot> GetRespectStateAsync(CancellationToken ct)
    {
        IServerConfigGrain config = this.GrainFactory.GetServerConfigGrain();
        int dailyLimit = await config
            .GetIntAsync(RespectConfig.DailyLimitKey, RespectConfig.DailyLimitDefault)
            .ConfigureAwait(true);
        int petDailyLimit = await config
            .GetIntAsync(RespectConfig.PetDailyLimitKey, RespectConfig.PetDailyLimitDefault)
            .ConfigureAwait(true);
        int replenishes = await config
            .GetIntAsync(RespectConfig.DailyReplenishesKey, RespectConfig.DailyReplenishesDefault)
            .ConfigureAwait(true);

        DateTime now = DateTime.UtcNow;

        return new PlayerRespectSnapshot
        {
            Received = _state.RespectReceived,
            Left = RespectBudget.Remaining(
                _state.RespectGivenToday,
                _state.RespectResetDate,
                now,
                dailyLimit
            ),
            PetLeft = RespectBudget.Remaining(
                _state.PetRespectGivenToday,
                _state.RespectResetDate,
                now,
                petDailyLimit
            ),
            ReplenishesLeft = RespectBudget.Remaining(
                _state.RespectReplenishesUsedToday,
                _state.RespectResetDate,
                now,
                replenishes
            ),
            MaxPerDay = dailyLimit,
        };
    }

    /// <summary>
    /// Moves all three respect counters onto <paramref name="day"/> together. They share one reset
    /// date, so whichever budget notices the new day first has to clear the other two — otherwise
    /// yesterday's pet scratches would still be counted against today.
    /// </summary>
    private void RollRespectDayIfNeeded(DateTime day)
    {
        if (_state.RespectResetDate?.Date == day.Date)
        {
            return;
        }

        _state.RespectResetDate = day;
        _state.RespectGivenToday = 0;
        _state.PetRespectGivenToday = 0;
        _state.RespectReplenishesUsedToday = 0;
    }

    public async Task<int> AddRespectReceivedAsync(CancellationToken ct)
    {
        _state.RespectReceived += 1;

        await WriteToDatabaseAsync(ct);

        return _state.RespectReceived;
    }

    public async Task<int> AddAchievementScoreAsync(int delta, CancellationToken ct)
    {
        if (delta == 0)
        {
            return _state.AchievementScore;
        }

        _state.AchievementScore += delta;

        await WriteToDatabaseAsync(ct);

        return _state.AchievementScore;
    }

    public async Task SetNameAsync(string name, CancellationToken ct)
    {
        // Captured before the write: the old name is what a report, a chatlog or a ban appeal
        // actually names, and after this line nothing else remembers it.
        string previousName = _state.Name;

        _state.Name = name;

        await WriteToDatabaseAsync(ct);

        await _grainFactory
            .GetPlayerDirectoryGrain()
            .SetPlayerNameAsync(PlayerId.Parse((int)this.GetPrimaryKeyLong()), name, ct);

        IPlayerPresenceGrain playerPresence = _grainFactory.GetPlayerPresenceGrain(
            (int)this.GetPrimaryKeyLong()
        );

        await playerPresence.OnPlayerUpdatedAsync(await GetSummaryAsync(ct), ct);

        await _events
            .PublishAsync(
                new PlayerNameChangedEvent(
                    PlayerId.Parse((int)this.GetPrimaryKeyLong()),
                    previousName,
                    name
                ),
                ct
            )
            .ConfigureAwait(true);
    }

    public Task<bool> IsNuxCompletedAsync(CancellationToken ct) =>
        Task.FromResult(_state.NuxCompletedAt is not null);

    public async Task MarkNuxCompletedAsync(CancellationToken ct)
    {
        if (_state.NuxCompletedAt is not null)
        {
            return;
        }

        _state.NuxCompletedAt = DateTime.UtcNow;

        await using VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct);

        await dbCtx
            .Players.Where(x => x.Id == (int)_state.PlayerId)
            .ExecuteUpdateAsync(
                up => up.SetProperty(p => p.NuxCompletedAt, _state.NuxCompletedAt),
                ct
            );
    }

    public async Task SetMottoAsync(string text, CancellationToken ct)
    {
        _state.Motto = text;

        await WriteToDatabaseAsync(ct);

        IPlayerPresenceGrain playerPresence = _grainFactory.GetPlayerPresenceGrain(
            (int)this.GetPrimaryKeyLong()
        );

        await playerPresence.OnPlayerUpdatedAsync(await GetSummaryAsync(ct), ct);

        await _events
            .PublishAsync(new PlayerMottoChangedEvent(_state.PlayerId, text), ct)
            .ConfigureAwait(true);
    }

    public Task<PlayerPerkFlags> GetPerksAsync(CancellationToken ct) =>
        Task.FromResult(_state.Perks);

    public async Task<bool> GrantPerkAsync(PlayerPerkFlags perk, CancellationToken ct)
    {
        // Already held is not a failure. Every caller is a reward that may be retried, and the
        // right answer to "grant it again" is that they have it.
        if (perk == PlayerPerkFlags.None || _state.Perks.HasFlag(perk))
        {
            return false;
        }

        _state.Perks |= perk;

        // The live flag and the row move together, in this method. A perk gates what the account
        // may do, and persisting one while the session still reads the old value is the exact shape
        // of the room-rights bug the contract calls out.
        await using VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct);

        await dbCtx
            .Players.Where(x => x.Id == (int)_state.PlayerId)
            .ExecuteUpdateAsync(up => up.SetProperty(p => p.PlayerPerks, _state.Perks), ct);

        _logger.LogInformation(
            "Granted perk {Perk} to player {PlayerId}.",
            perk,
            (int)_state.PlayerId
        );

        return true;
    }

    public async Task SetChatStylePreferenceAsync(int chatStyle, CancellationToken ct)
    {
        // No-op when unchanged so a repeated toggle from the client never touches the database.
        if (_state.RoomChatStyleId == chatStyle)
        {
            return;
        }

        _state.RoomChatStyleId = chatStyle;

        // Targeted single-column update: RoomChatStyleId is not part of the shared
        // WriteToDatabaseAsync property set, so writing it here can neither clobber nor be clobbered
        // by the other player fields.
        await using VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct);

        await dbCtx
            .Players.Where(p => p.Id == (int)_state.PlayerId)
            .ExecuteUpdateAsync(up => up.SetProperty(p => p.RoomChatStyleId, chatStyle), ct)
            .ConfigureAwait(true);
    }

    public Task<int> GetChatStylePreferenceAsync(CancellationToken ct) =>
        Task.FromResult(_state.RoomChatStyleId);

    public Task<PlayerSummarySnapshot> GetSummaryAsync(CancellationToken ct)
    {
        return Task.FromResult(
            new PlayerSummarySnapshot
            {
                PlayerId = _state.PlayerId,
                Name = _state.Name,
                Motto = _state.Motto,
                Figure = _state.Figure,
                Gender = _state.Gender,
                AchievementScore = _state.AchievementScore,
                CreatedAt = _state.CreatedAt,
                MutedUntilUtc = _state.MutedUntil,
                FavouriteGroupId = _state.FavouriteGroupId,
                FavouriteGroupName = _state.FavouriteGroupName,
                SafetyLocked = _state.SafetyLocked,
            }
        );
    }

    public Task<bool> IsSafetyLockedAsync(CancellationToken ct) =>
        Task.FromResult(_state.SafetyLocked);

    public async Task OnAccountSafetyLockChangedAsync(bool locked, CancellationToken ct)
    {
        if (_state.SafetyLocked == locked)
        {
            return;
        }

        _state.SafetyLocked = locked;

        // 0 is LOCKED on this wire — see the composer, where the constant lives so nobody has to
        // remember it.
        await _grainFactory
            .GetPlayerPresenceGrain((long)_state.PlayerId)
            .SendComposerAsync(AccountSafetyLockStatusChangeMessageComposer.For(locked))
            .ConfigureAwait(true);
    }

    public async Task SetFavouriteGroupAsync(int groupId, CancellationToken ct)
    {
        if (_state.FavouriteGroupId == groupId)
        {
            return;
        }

        int? persistedValue = groupId > 0 ? groupId : null;

        await using VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct);

        await dbCtx
            .Players.Where(p => p.Id == (int)_state.PlayerId)
            .ExecuteUpdateAsync(up => up.SetProperty(p => p.FavouriteGroupId, persistedValue), ct)
            .ConfigureAwait(true);

        _state.FavouriteGroupId = groupId;
        _state.FavouriteGroupName =
            groupId > 0
                ? await dbCtx
                    .Groups.AsNoTracking()
                    .Where(g => g.Id == groupId && g.DeletedAt == null)
                    .Select(g => g.Name)
                    .FirstOrDefaultAsync(ct)
                    .ConfigureAwait(true)
                    ?? string.Empty
                : string.Empty;

        // Re-badge the avatar in place. Without this the new badge only appears the next time the
        // player re-enters a room, because the avatar snapshot is built once on entry.
        RoomPointerSnapshot activeRoom = await _grainFactory
            .GetPlayerPresenceGrain(_state.PlayerId)
            .GetActiveRoomAsync()
            .ConfigureAwait(true);

        if (activeRoom.RoomId.Value <= 0)
        {
            return;
        }

        try
        {
            await _grainFactory
                .GetRoomSecurity(activeRoom.RoomId)
                .UpdateAvatarFavouriteGroupAsync(
                    _state.PlayerId,
                    _state.FavouriteGroupId,
                    _state.FavouriteGroupName,
                    ct
                )
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to push favourite-guild badge for player {PlayerId} into room {RoomId}.",
                _state.PlayerId,
                activeRoom.RoomId
            );
        }
    }

    public async Task<PlayerExtendedProfileSnapshot> GetExtendedProfileSnapshotAsync(
        PlayerId viewerId,
        CancellationToken ct
    )
    {
        int playerId = _state.PlayerId;
        int viewer = viewerId;

        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(true);

        // One context, five counts. Everything below was a constant until now, which is why a
        // profile showed nobody as a friend and every account as level 1.
        DateTime? lastLoginAt = await dbCtx
            .Players.AsNoTracking()
            .Where(p => p.Id == playerId)
            .Select(p => p.LastLoginAt)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(true);

        int friendCount = await dbCtx
            .MessengerFriends.AsNoTracking()
            .CountAsync(f => f.PlayerEntityId == playerId, ct)
            .ConfigureAwait(true);

        // Looking at your own profile is not a friendship; without this the client would offer to
        // befriend yourself.
        bool isFriend =
            viewer != playerId
            && await dbCtx
                .MessengerFriends.AsNoTracking()
                .AnyAsync(f => f.PlayerEntityId == viewer && f.FriendPlayerEntityId == playerId, ct)
                .ConfigureAwait(true);

        // Only the viewer's own outgoing request counts: a request this player sent to the viewer
        // is the viewer's to accept, not an "already asked" on this button.
        bool requestSent =
            !isFriend
            && viewer != playerId
            && await dbCtx
                .MessengerRequests.AsNoTracking()
                .AnyAsync(
                    r => r.PlayerEntityId == viewer && r.RequestedPlayerEntityId == playerId,
                    ct
                )
                .ConfigureAwait(true);

        int totalBadges = await dbCtx
            .PlayerBadges.AsNoTracking()
            .CountAsync(b => b.PlayerEntityId == playerId, ct)
            .ConfigureAwait(true);

        // The client shows how many achievement levels have been cleared, so the levels sum rather
        // than the number of achievements touched.
        int achievementLevels = await dbCtx
            .PlayerAchievements.AsNoTracking()
            .Where(a => a.PlayerEntityId == playerId)
            .SumAsync(a => a.Level, ct)
            .ConfigureAwait(true);

        return new PlayerExtendedProfileSnapshot
        {
            UserId = _state.PlayerId,
            UserName = _state.Name,
            Figure = _state.Figure,
            Motto = _state.Motto,
            CreationDate = _state.CreatedAt.ToString("yyyy-MM-dd"),
            AchievementScore = _state.AchievementScore,
            FriendCount = friendCount,
            IsFriend = isFriend,
            IsFriendRequestSent = requestSent,
            IsOnline = true,
            Guilds = [],
            LastAccessSinceInSeconds = SecondsSince(lastLoginAt),
            OpenProfileWindow = true,
            IsHidden = false,
            // Resolved from the achievement score against the configured ladder. This was the
            // constant 1, which is why every profile in the hotel read "Level 1" no matter what
            // the player had done.
            AccountLevel = _accountLevels.ResolveLevel(_state.AchievementScore),
            IntegerField24 = 0,
            StarGemCount = 0,
            BooleanField26 = false,
            BooleanField27 = false,
            TotalBadges = totalBadges,
            AchievementLevel = achievementLevels,
        };
    }

    /// <summary>
    /// Seconds since the player last logged in, or 0 when they never have. The client renders it as
    /// "last seen", so a player with no recorded login reads as "just now" rather than as a date in
    /// 1970.
    /// </summary>
    private static int SecondsSince(DateTime? moment)
    {
        if (moment is not { } instant)
        {
            return 0;
        }

        double seconds = (DateTime.UtcNow - instant).TotalSeconds;

        return seconds <= 0 ? 0 : (int)Math.Min(int.MaxValue, seconds);
    }

    public Task<ClubSubscriptionSnapshot> GetClubSubscriptionAsync(CancellationToken ct)
    {
        bool isActive = _state.ClubLevel > 0 && _state.ClubExpiresAt > DateTime.UtcNow;
        int daysLeft = isActive ? (int)(_state.ClubExpiresAt - DateTime.UtcNow).TotalDays : 0;

        return Task.FromResult(
            new ClubSubscriptionSnapshot
            {
                IsActive = isActive,
                IsVip = isActive && _state.ClubLevel >= 2,
                ExpiresAt = _state.ClubExpiresAt,
                DaysLeft = daysLeft,
                TotalMonths = _state.ClubTotalMonths,
                GiftsAvailable = _state.ClubGiftsAvailable,
                NextGiftAt = _state.ClubNextGiftAt,
                PaydayAt = _state.KickbackPaydayAt,
                CreditsSpentThisPeriod = _state.KickbackCreditsSpent,
                TotalCreditsRewarded = _state.KickbackTotalRewarded,
                TotalCreditsSpent = _state.KickbackTotalSpent,
                PastClubDays = _state.ClubPastClubDays,
                PastVipDays = _state.ClubPastVipDays,
            }
        );
    }

    public async Task<ClubPurchaseResult> PurchaseClubAsync(
        int months,
        bool isVip,
        int costCredits,
        CancellationToken ct
    )
    {
        if (months <= 0)
        {
            return ClubPurchaseResult.Success;
        }

        List<WalletDebitRequest> debitRequests =
            costCredits > 0
                ?
                [
                    new WalletDebitRequest
                    {
                        CurrencyKind = new CurrencyKind { CurrencyType = CurrencyType.Credits },
                        Amount = costCredits,
                    },
                ]
                : [];

        CommerceOperationId operation = CommerceOperationId.New();

        await _journal
            .OpenAsync(
                operation,
                CommerceOperationKind.ClubPurchase,
                (int)_state.PlayerId,
                $"club months={months} vip={isVip} cost={costCredits}",
                ct
            )
            .ConfigureAwait(true);

        // Applying the months writes subscription rows, grants badges and raises events, any of
        // which can throw. This used to sit after a bare debit, so a failure in there took the
        // player's credits and left them without the membership they paid for.
        WalletPurchaseResult<ClubPurchaseResult> result = await _grainFactory
            .GetPlayerWalletGrain(_state.PlayerId)
            .ExecutePurchaseAsync(
                debitRequests,
                operation,
                async innerCt =>
                {
                    await _journal
                        .TransitionAsync(
                            operation,
                            CommerceOperationState.Debited,
                            CommerceStepKeys.DEBIT,
                            null,
                            innerCt
                        )
                        .ConfigureAwait(true);

                    ClubPurchaseResult applied = await ApplyClubMonthsAsync(
                            months,
                            isVip,
                            costCredits,
                            operation,
                            innerCt
                        )
                        .ConfigureAwait(true);

                    // The months are on the account: badges granted, events raised, and the client
                    // already told. Refunding past here would leave a member who paid nothing.
                    await _journal
                        .TransitionAsync(
                            operation,
                            CommerceOperationState.Completed,
                            CommerceStepKeys.CLUB_MONTHS,
                            null,
                            innerCt
                        )
                        .ConfigureAwait(true);

                    return applied;
                },
                _logger,
                ct,
                _journal
            )
            .ConfigureAwait(true);

        if (!result.Succeeded)
        {
            await _journal
                .TransitionAsync(
                    operation,
                    CommerceOperationState.FailedBeforePivot,
                    CommerceStepKeys.DEBIT,
                    "insufficient balance",
                    ct
                )
                .ConfigureAwait(true);

            return ClubPurchaseResult.NotEnoughCredits;
        }

        return result.Reward!;
    }

    public Task<ClubPurchaseResult> GrantClubMonthsAsync(
        int months,
        bool isVip,
        CommerceOperationId operation,
        CancellationToken ct
    ) =>
        months <= 0
            ? Task.FromResult(ClubPurchaseResult.Success)
            : ApplyClubMonthsAsync(months, isVip, costCredits: 0, operation, ct);

    /// <summary>
    /// Extends the membership and everything hanging off it (streak, gifts, badges, kickback, the
    /// subscription row). Split out of <see cref="PurchaseClubAsync"/> so a free grant -- a mystery
    /// box prize, a staff award -- reuses the exact same bookkeeping instead of a second, drifting
    /// copy of it. The caller is responsible for taking payment first, if any.
    /// </summary>
    private async Task<ClubPurchaseResult> ApplyClubMonthsAsync(
        int months,
        bool isVip,
        int costCredits,
        CommerceOperationId operation,
        CancellationToken ct
    )
    {
        // Asked before anything is touched, because the months are added to whatever is already
        // there: repeating this call without the check is how a replayed webhook buys a month twice.
        // The receipt goes in with the subscription row further down, so "the months are there" and
        // "a receipt says so" are one commit and cannot disagree.
        if (!operation.IsNone)
        {
            await using VortexDbContext receiptCtx = await _dbCtxFactory.CreateDbContextAsync(ct);

            if (
                await receiptCtx
                    .HasCommittedReceiptAsync(operation, CommerceStepKeys.CLUB_MONTHS, ct)
                    .ConfigureAwait(true)
            )
            {
                return ClubPurchaseResult.Success;
            }
        }

        IServerConfigGrain config = this.GrainFactory.GetServerConfigGrain();
        int giftCycleDays = await config
            .GetIntAsync(ClubConfig.GiftCycleDaysKey, ClubConfig.GiftCycleDaysDefault)
            .ConfigureAwait(true);
        int streakGraceDays = await config
            .GetIntAsync(ClubConfig.StreakGraceDaysKey, ClubConfig.StreakGraceDaysDefault)
            .ConfigureAwait(true);

        DateTime now = DateTime.UtcNow;
        bool isRenewal = _state.ClubLevel > 0 && _state.ClubExpiresAt > now;

        // Streak (consecutive membership months) keeps running while active, and survives a short
        // lapse (grace window). A longer gap resets the streak so it starts over from this purchase.
        int newTotalMonths;
        if (isRenewal)
        {
            newTotalMonths = _state.ClubTotalMonths + months;
        }
        else
        {
            bool hadPriorSub = _state.ClubExpiresAt > DateTime.MinValue;
            double lapsedDays = hadPriorSub
                ? (now - _state.ClubExpiresAt).TotalDays
                : double.MaxValue;
            newTotalMonths =
                lapsedDays > streakGraceDays ? months : _state.ClubTotalMonths + months;
        }

        DateTime baseDate = isRenewal ? _state.ClubExpiresAt : now;
        DateTime newExpiry = baseDate.AddMonths(months);
        int newLevel = isVip ? 2 : 1;
        int newGiftsAvailable = _state.ClubGiftsAvailable + months;

        // First purchase or expired sub: gift cycle starts now + 31 days.
        // Renewal: keep existing cycle so the player doesn't lose their countdown.
        DateTime? newNextGiftAt =
            isRenewal && _state.ClubNextGiftAt.HasValue
                ? _state.ClubNextGiftAt
                : now.AddDays(giftCycleDays);

        // Lifetime membership days (never reset, VIP tracked separately) feed the club info window.
        _state.ClubPastClubDays += months * giftCycleDays;
        if (isVip)
        {
            _state.ClubPastVipDays += months * giftCycleDays;
        }

        _state.ClubLevel = newLevel;
        _state.ClubExpiresAt = newExpiry;
        _state.ClubTotalMonths = newTotalMonths;
        _state.ClubGiftsAvailable = newGiftsAvailable;
        _state.ClubNextGiftAt = newNextGiftAt;
        _state.ClubLastExpiredAt = null;
        _state.ClubFirstSubscribedAt ??= now;

        // Grant the membership badge(s) once active. Idempotent at the inventory layer.
        await GrantClubBadgesAsync(isVip, ct).ConfigureAwait(true);

        // Init payday on first subscription; renewals keep the existing cycle
        if (_state.KickbackPaydayAt is null)
        {
            _state.KickbackPaydayAt = now.AddDays(giftCycleDays);
        }

        await using VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct);

        PlayerSubscriptionEntity? existing = await dbCtx.PlayerSubscriptions.FirstOrDefaultAsync(
            x =>
                x.PlayerEntityId == (int)_state.PlayerId
                && x.SubscriptionType == SubscriptionType.HabboClub,
            ct
        );

        if (existing is not null)
        {
            existing.Level = newLevel;
            existing.ExpiresAt = newExpiry;
            existing.TotalMonths = newTotalMonths;
            existing.GiftsAvailable = newGiftsAvailable;
            existing.NextGiftAt = newNextGiftAt;
            existing.PastClubDays = _state.ClubPastClubDays;
            existing.PastVipDays = _state.ClubPastVipDays;
            existing.FirstSubscribedAt = _state.ClubFirstSubscribedAt;
            existing.LastExpiredAt = null;
            existing.HcBadgeGranted = _state.ClubBadgeGranted;
        }
        else
        {
            PlayerEntity playerEntity =
                await dbCtx.Players.FindAsync([_state.PlayerId.Value], ct)
                ?? throw new VortexException(VortexErrorCodeEnum.PlayerNotFound);

            dbCtx.PlayerSubscriptions.Add(
                new PlayerSubscriptionEntity
                {
                    PlayerEntityId = _state.PlayerId,
                    SubscriptionType = SubscriptionType.HabboClub,
                    Level = newLevel,
                    ExpiresAt = newExpiry,
                    TotalMonths = newTotalMonths,
                    GiftsAvailable = newGiftsAvailable,
                    NextGiftAt = newNextGiftAt,
                    PastClubDays = _state.ClubPastClubDays,
                    PastVipDays = _state.ClubPastVipDays,
                    FirstSubscribedAt = _state.ClubFirstSubscribedAt,
                    HcBadgeGranted = _state.ClubBadgeGranted,
                    PlayerEntity = playerEntity,
                }
            );
        }

        // In the same commit as the months. This is what the caller used to have to fake by claiming
        // the step beforehand: the claim and the grant were two writes, and a crash between them left
        // the months owed with the step already marked done -- a paid order indistinguishable from a
        // delivered one, and a replayed webhook that closed it as Fulfilled.
        if (!operation.IsNone)
        {
            dbCtx.CommerceReceipts.Add(
                new CommerceReceiptEntity
                {
                    OperationId = operation.Value,
                    StepKey = CommerceStepKeys.CLUB_MONTHS,
                    Result = months.ToString(CultureInfo.InvariantCulture),
                    CreatedAt = DateTime.UtcNow,
                }
            );
        }

        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

        await UpsertKickbackAsync(ct).ConfigureAwait(true);

        await _events
            .PublishAsync(
                new ClubPurchasedEvent(
                    _state.PlayerId,
                    months,
                    isVip,
                    isRenewal,
                    costCredits,
                    newTotalMonths
                ),
                ct
            )
            .ConfigureAwait(true);

        return ClubPurchaseResult.Success;
    }

    public async Task<bool> TryConsumeClubGiftAsync(string productCode, CancellationToken ct)
    {
        if (_state.ClubGiftsAvailable <= 0)
        {
            return false;
        }

        _state.ClubGiftsAvailable--;

        await using VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct);

        await dbCtx
            .PlayerSubscriptions.Where(x =>
                x.PlayerEntityId == (int)_state.PlayerId
                && x.SubscriptionType == SubscriptionType.HabboClub
            )
            .ExecuteUpdateAsync(
                up => up.SetProperty(p => p.GiftsAvailable, _state.ClubGiftsAvailable),
                ct
            )
            .ConfigureAwait(true);

        await _events
            .PublishAsync(new ClubGiftClaimedEvent(_state.PlayerId, productCode), ct)
            .ConfigureAwait(true);

        return true;
    }

    public async Task TrackCreditSpendAsync(int credits, CancellationToken ct)
    {
        if (credits <= 0 || _state.ClubLevel == 0)
        {
            return;
        }

        _state.KickbackCreditsSpent += credits;
        _state.KickbackTotalSpent += credits;

        await UpsertKickbackAsync(ct).ConfigureAwait(true);
    }

    public async Task<bool> ApplyAccountBanAsync(
        int actorPlayerId,
        DateTime? bannedUntil,
        string reason,
        CancellationToken ct
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct);

        int? accountId = await FindLinkedAccountIdAsync(dbCtx, ct).ConfigureAwait(true);

        if (accountId is null)
        {
            return false;
        }

        // IgnoreQueryFilters: an unban soft-deletes the row, so a later re-ban must find and revive
        // that soft-deleted row rather than insert a duplicate under the unique account index.
        AccountBanEntity? existing = await dbCtx
            .AccountBans.IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.PlayerAccountEntityId == accountId, ct)
            .ConfigureAwait(true);

        if (bannedUntil is null)
        {
            if (existing is not null)
            {
                existing.DeletedAt = DateTime.UtcNow;
                await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);
            }
        }
        else if (existing is null)
        {
            dbCtx.AccountBans.Add(
                new AccountBanEntity
                {
                    PlayerAccountEntityId = accountId.Value,
                    DateExpires = bannedUntil.Value,
                    Reason = reason,
                    PlayerAccountEntity = null!,
                }
            );
            await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);
        }
        else
        {
            existing.DateExpires = bannedUntil.Value;
            existing.Reason = reason;
            existing.DeletedAt = null;
            await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);
        }

        await _events
            .PublishAsync(
                new PlayerAccountBannedEvent(
                    actorPlayerId,
                    (int)_state.PlayerId,
                    bannedUntil,
                    reason
                ),
                ct
            )
            .ConfigureAwait(true);

        return true;
    }

    public async Task<bool> ApplyTradingLockAsync(
        int actorPlayerId,
        DateTime? lockedUntil,
        CancellationToken ct
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct);

        PlayerEntity? player = await dbCtx
            .Players.FindAsync([_state.PlayerId.Value], ct)
            .ConfigureAwait(true);

        if (player is null)
        {
            return false;
        }

        player.TradingLockedUntil = lockedUntil;

        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

        await _events
            .PublishAsync(
                new PlayerTradingLockedEvent(actorPlayerId, (int)_state.PlayerId, lockedUntil),
                ct
            )
            .ConfigureAwait(true);

        return true;
    }

    public async Task<DateTime?> ApplyHotelMuteAsync(
        int actorPlayerId,
        DateTime? mutedUntil,
        CancellationToken ct
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct);

        PlayerEntity? player = await dbCtx
            .Players.FindAsync([_state.PlayerId.Value], ct)
            .ConfigureAwait(true);

        if (player is null)
        {
            return null;
        }

        player.MutedUntil = mutedUntil;

        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

        // Cached in the same method that persists it: the entry snapshot is read from this field,
        // so leaving it stale would mute the player everywhere except the next room they walk into.
        _state.MutedUntil = mutedUntil;

        await _events
            .PublishAsync(
                new PlayerHotelMutedEvent(actorPlayerId, (int)_state.PlayerId, mutedUntil),
                ct
            )
            .ConfigureAwait(true);

        return mutedUntil;
    }

    public async Task<DateTime?> GetActiveBanExpiryAsync(CancellationToken ct)
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct);

        int? accountId = await FindLinkedAccountIdAsync(dbCtx, ct).ConfigureAwait(true);

        if (accountId is null)
        {
            return null;
        }

        DateTime now = DateTime.UtcNow;

        AccountBanEntity? activeBan = await dbCtx
            .AccountBans.AsNoTracking()
            .FirstOrDefaultAsync(
                b =>
                    b.PlayerAccountEntityId == accountId
                    && b.DeletedAt == null
                    && b.DateExpires > now,
                ct
            )
            .ConfigureAwait(true);

        return activeBan?.DateExpires;
    }

    private async Task<int?> FindLinkedAccountIdAsync(VortexDbContext dbCtx, CancellationToken ct)
    {
        PlayerEntity? player = await dbCtx
            .Players.FindAsync([_state.PlayerId.Value], ct)
            .ConfigureAwait(true);

        return player?.PlayerAccountEntityId;
    }

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await HydrateAsync(ct);

        TimeSpan maintenanceInterval = TimeSpan.FromMilliseconds(_clubConfig.MaintenanceIntervalMs);

        this.RegisterGrainTimer<object?>(
            async (_, ct) => await RunMaintenanceAsync(ct),
            null,
            maintenanceInterval,
            maintenanceInterval
        );
    }

    /// <summary>
    ///     Hourly club maintenance: detect expiry, grant due gift tokens, and pay out kickback.
    ///     Each step is isolated so one failing step never blocks the others, and failures are logged
    ///     rather than silently swallowed by the timer runtime.
    /// </summary>
    private async Task RunMaintenanceAsync(CancellationToken ct)
    {
        try
        {
            await CheckExpirationAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Club expiry check failed for player {PlayerId}",
                (int)_state.PlayerId
            );
        }

        try
        {
            await CheckAndGrantGiftTokensAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Club gift grant failed for player {PlayerId}",
                (int)_state.PlayerId
            );
        }

        try
        {
            await CheckAndGrantPaydayAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Club payday failed for player {PlayerId}", (int)_state.PlayerId);
        }
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct)
    {
        await WriteToDatabaseAsync(ct);
        await UpsertKickbackAsync(ct);
    }

    private async Task CheckAndGrantGiftTokensAsync(CancellationToken ct)
    {
        if (_state.ClubLevel == 0 || _state.ClubExpiresAt <= DateTime.UtcNow)
        {
            return;
        }

        if (_state.ClubNextGiftAt is null || _state.ClubNextGiftAt > DateTime.UtcNow)
        {
            return;
        }

        int giftCycleDays = await this
            .GrainFactory.GetServerConfigGrain()
            .GetIntAsync(ClubConfig.GiftCycleDaysKey, ClubConfig.GiftCycleDaysDefault)
            .ConfigureAwait(true);

        int tokensToGrant = 0;
        DateTime nextGift = _state.ClubNextGiftAt.Value;
        DateTime now = DateTime.UtcNow;

        while (nextGift <= now)
        {
            tokensToGrant++;
            nextGift = nextGift.AddDays(giftCycleDays);
        }

        _state.ClubGiftsAvailable += tokensToGrant;
        _state.ClubNextGiftAt = nextGift;

        await using VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct);

        await dbCtx
            .PlayerSubscriptions.Where(x =>
                x.PlayerEntityId == (int)_state.PlayerId
                && x.SubscriptionType == SubscriptionType.HabboClub
            )
            .ExecuteUpdateAsync(
                up =>
                    up.SetProperty(p => p.GiftsAvailable, _state.ClubGiftsAvailable)
                        .SetProperty(p => p.NextGiftAt, _state.ClubNextGiftAt),
                ct
            )
            .ConfigureAwait(true);

        await _events
            .PublishAsync(
                new ClubGiftTokenGrantedEvent(
                    _state.PlayerId,
                    tokensToGrant,
                    _state.ClubGiftsAvailable
                ),
                ct
            )
            .ConfigureAwait(true);
    }

    private async Task HydrateAsync(CancellationToken ct)
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct);

        PlayerEntity entity =
            await dbCtx
                .Players.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == (int)_state.PlayerId, ct)
            ?? throw new VortexException(VortexErrorCodeEnum.PlayerNotFound);

        _state.Name = entity.Name;
        _state.Motto = entity.Motto ?? string.Empty;
        _state.Figure = entity.Figure;
        _state.Gender = entity.Gender;
        _state.RoomChatStyleId = entity.RoomChatStyleId ?? 0;
        _state.Perks = entity.PlayerPerks;
        _state.AchievementScore = entity.AchievementScore;
        _state.RespectReceived = entity.RespectReceived;
        _state.RespectGivenToday = entity.RespectGivenToday;
        _state.PetRespectGivenToday = entity.PetRespectGivenToday;
        _state.RespectReplenishesUsedToday = entity.RespectReplenishesUsedToday;
        _state.RespectResetDate = entity.RespectResetDate;
        _state.CreatedAt = entity.CreatedAt;
        _state.MutedUntil = entity.MutedUntil;
        _state.NuxCompletedAt = entity.NuxCompletedAt;
        _state.LastUpdated = entity.UpdatedAt;

        // The safety lock lives on the ACCOUNT — every avatar it owns spends the same purse — so it
        // is read across the key rather than off the player row. Held in state because the spending
        // handlers ask for it on every purchase, and a database round trip per click, to answer a
        // flag that changes once in an account's life, is not a trade worth making. The website
        // keeps it in step through OnAccountSafetyLockChangedAsync.
        _state.SafetyLocked =
            entity.PlayerAccountEntityId is int accountId
            && await dbCtx
                .PlayerAccounts.AsNoTracking()
                .Where(a => a.Id == accountId)
                .Select(a => a.SafetyLocked)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(true);

        _state.FavouriteGroupId = entity.FavouriteGroupId ?? 0;
        _state.FavouriteGroupName =
            _state.FavouriteGroupId > 0
                ? await dbCtx
                    .Groups.AsNoTracking()
                    .Where(g => g.Id == _state.FavouriteGroupId && g.DeletedAt == null)
                    .Select(g => g.Name)
                    .FirstOrDefaultAsync(ct)
                    .ConfigureAwait(true)
                    ?? string.Empty
                : string.Empty;

        PlayerSubscriptionEntity? sub = await dbCtx
            .PlayerSubscriptions.AsNoTracking()
            .FirstOrDefaultAsync(
                x =>
                    x.PlayerEntityId == (int)_state.PlayerId
                    && x.SubscriptionType == SubscriptionType.HabboClub,
                ct
            );

        if (sub is not null)
        {
            // Lifetime / streak bookkeeping is loaded regardless of active state.
            _state.ClubTotalMonths = sub.TotalMonths;
            _state.ClubPastClubDays = sub.PastClubDays;
            _state.ClubPastVipDays = sub.PastVipDays;
            _state.ClubFirstSubscribedAt = sub.FirstSubscribedAt;
            _state.ClubLastExpiredAt = sub.LastExpiredAt;
            _state.ClubBadgeGranted = sub.HcBadgeGranted;
            _state.ClubExpiresAt = sub.ExpiresAt;

            if (sub.ExpiresAt > DateTime.UtcNow)
            {
                _state.ClubLevel = sub.Level;
                _state.ClubGiftsAvailable = sub.GiftsAvailable;
                _state.ClubNextGiftAt = sub.NextGiftAt;

                await CheckAndGrantGiftTokensAsync(ct);
            }
            else if (sub.Level > 0)
            {
                // Membership lapsed while offline — surface the expiry once.
                _state.ClubLevel = sub.Level;
                await CheckExpirationAsync(ct);
            }
        }

        PlayerKickbackEntity? kickback = await dbCtx
            .PlayerKickbacks.AsNoTracking()
            .FirstOrDefaultAsync(x => x.PlayerEntityId == (int)_state.PlayerId, ct);

        if (kickback is not null)
        {
            _state.KickbackPaydayAt = kickback.PaydayAt;
            _state.KickbackCreditsSpent = kickback.CreditsSpent;
            _state.KickbackTotalRewarded = kickback.TotalRewarded;
            _state.KickbackTotalSpent = kickback.TotalSpent;

            await CheckAndGrantPaydayAsync(ct);
        }

        await _grainFactory
            .GetPlayerDirectoryGrain()
            .SetPlayerNameAsync(PlayerId.Parse((int)this.GetPrimaryKeyLong()), _state.Name, ct);
    }

    private async Task WriteToDatabaseAsync(CancellationToken ct)
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct);

        PlayerSummarySnapshot snapshot = await GetSummaryAsync(ct);

        await dbCtx
            .Players.Where(x => x.Id == (int)_state.PlayerId)
            .ExecuteUpdateAsync(
                up =>
                    up.SetProperty(p => p.Name, snapshot.Name)
                        .SetProperty(p => p.Motto, snapshot.Motto)
                        .SetProperty(p => p.Figure, snapshot.Figure)
                        .SetProperty(p => p.Gender, snapshot.Gender)
                        .SetProperty(p => p.AchievementScore, snapshot.AchievementScore)
                        .SetProperty(p => p.RespectReceived, _state.RespectReceived)
                        .SetProperty(p => p.RespectGivenToday, _state.RespectGivenToday)
                        .SetProperty(p => p.PetRespectGivenToday, _state.PetRespectGivenToday)
                        .SetProperty(
                            p => p.RespectReplenishesUsedToday,
                            _state.RespectReplenishesUsedToday
                        )
                        .SetProperty(p => p.RespectResetDate, _state.RespectResetDate),
                ct
            );

        _state.LastUpdated = DateTime.UtcNow;
    }

    private async Task GrantClubBadgesAsync(bool isVip, CancellationToken ct)
    {
        try
        {
            IInventoryGrain inventory = _grainFactory.GetInventoryGrain(
                (int)this.GetPrimaryKeyLong()
            );

            IServerConfigGrain config = this.GrainFactory.GetServerConfigGrain();
            string clubBadgeCode =
                await config.GetValueAsync(ClubConfig.ClubBadgeCodeKey).ConfigureAwait(true)
                ?? ClubConfig.ClubBadgeCodeDefault;
            string vipBadgeCode =
                await config.GetValueAsync(ClubConfig.VipBadgeCodeKey).ConfigureAwait(true)
                ?? ClubConfig.VipBadgeCodeDefault;

            if (!_state.ClubBadgeGranted)
            {
                await inventory.GrantBadgeAsync(clubBadgeCode, ct).ConfigureAwait(true);
                _state.ClubBadgeGranted = true;
            }

            if (isVip)
            {
                await inventory.GrantBadgeAsync(vipBadgeCode, ct).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to grant club badge for player {PlayerId}",
                (int)_state.PlayerId
            );
        }
    }

    private async Task CheckExpirationAsync(CancellationToken ct)
    {
        if (_state.ClubLevel == 0)
        {
            return;
        }

        if (_state.ClubExpiresAt > DateTime.UtcNow)
        {
            return;
        }

        // Already surfaced this exact expiry — avoid firing the event twice.
        if (_state.ClubLastExpiredAt == _state.ClubExpiresAt)
        {
            return;
        }

        bool wasVip = _state.ClubLevel >= 2;
        _state.ClubLastExpiredAt = _state.ClubExpiresAt;
        _state.ClubLevel = 0;

        await using VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct);

        await dbCtx
            .PlayerSubscriptions.Where(x =>
                x.PlayerEntityId == (int)_state.PlayerId
                && x.SubscriptionType == SubscriptionType.HabboClub
            )
            .ExecuteUpdateAsync(
                up => up.SetProperty(p => p.LastExpiredAt, _state.ClubLastExpiredAt),
                ct
            )
            .ConfigureAwait(true);

        await _events
            .PublishAsync(new ClubExpiredEvent(_state.PlayerId, wasVip), ct)
            .ConfigureAwait(true);

        _logger.LogInformation(
            "Club membership expired for player {PlayerId} (wasVip={WasVip})",
            (int)_state.PlayerId,
            wasVip
        );
    }

    private async Task CheckAndGrantPaydayAsync(CancellationToken ct)
    {
        if (_state.ClubLevel == 0 || _state.ClubExpiresAt <= DateTime.UtcNow)
        {
            return;
        }

        if (_state.KickbackPaydayAt is null || _state.KickbackPaydayAt > DateTime.UtcNow)
        {
            return;
        }

        IServerConfigGrain config = this.GrainFactory.GetServerConfigGrain();
        int kickbackPercent = await config
            .GetIntAsync(ClubConfig.KickbackPercentKey, ClubConfig.KickbackPercentDefault)
            .ConfigureAwait(true);
        int giftCycleDays = await config
            .GetIntAsync(ClubConfig.GiftCycleDaysKey, ClubConfig.GiftCycleDaysDefault)
            .ConfigureAwait(true);

        DateTime now = DateTime.UtcNow;
        DateTime payday = _state.KickbackPaydayAt.Value;

        while (payday <= now)
        {
            int monthlyReward = (int)(_state.KickbackCreditsSpent * (kickbackPercent / 100.0));
            int streakBonus = Math.Min(_state.ClubTotalMonths, giftCycleDays);
            int totalReward = monthlyReward + streakBonus;

            if (totalReward > 0)
            {
                IPlayerWalletGrain wallet = _grainFactory.GetPlayerWalletGrain(
                    (int)this.GetPrimaryKeyLong()
                );
                await wallet.GrantCreditsAsync(totalReward, ct).ConfigureAwait(true);
                _state.KickbackTotalRewarded += totalReward;

                await _events
                    .PublishAsync(new ClubPaydayEvent(_state.PlayerId, totalReward), ct)
                    .ConfigureAwait(true);
            }

            _state.KickbackCreditsSpent = 0;
            payday = payday.AddDays(giftCycleDays);
        }

        _state.KickbackPaydayAt = payday;

        await UpsertKickbackAsync(ct).ConfigureAwait(true);
    }

    private async Task UpsertKickbackAsync(CancellationToken ct)
    {
        if (_state.KickbackPaydayAt is null)
        {
            return;
        }

        await using VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct);

        PlayerKickbackEntity? existing = await dbCtx.PlayerKickbacks.FirstOrDefaultAsync(
            x => x.PlayerEntityId == (int)_state.PlayerId,
            ct
        );

        if (existing is not null)
        {
            existing.PaydayAt = _state.KickbackPaydayAt;
            existing.CreditsSpent = _state.KickbackCreditsSpent;
            existing.TotalRewarded = _state.KickbackTotalRewarded;
            existing.TotalSpent = _state.KickbackTotalSpent;
        }
        else
        {
            PlayerEntity playerEntity =
                await dbCtx.Players.FindAsync([(int)_state.PlayerId], ct)
                ?? throw new VortexException(VortexErrorCodeEnum.PlayerNotFound);

            dbCtx.PlayerKickbacks.Add(
                new PlayerKickbackEntity
                {
                    PlayerEntityId = _state.PlayerId,
                    PaydayAt = _state.KickbackPaydayAt,
                    CreditsSpent = _state.KickbackCreditsSpent,
                    TotalRewarded = _state.KickbackTotalRewarded,
                    TotalSpent = _state.KickbackTotalSpent,
                    PlayerEntity = playerEntity,
                }
            );
        }

        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);
    }
}
