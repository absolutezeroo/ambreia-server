using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Entities.Groups;
using Vortex.Database.Entities.Players;
using Vortex.Database.Entities.Room;
using Vortex.Events.Registry;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Events;
using Vortex.Primitives.Navigator.Enums;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Players.Wallet;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Server.Grains;
using Vortex.Rooms.Tests.Support;
using Vortex.Social.Grains;
using Xunit;

namespace Vortex.Rooms.Tests.Groups;

public sealed class GroupDirectoryGrainCreationTests
{
    private const string CORRELATION_ID = "group-create-correlation";

    [Fact]
    public async Task CreateGroupAsync_CancelledCreatingEvent_DoesNotDebitOrWriteRows()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        await SeedOwnerRoomAsync(options, ownerId: 7, roomId: 70).ConfigureAwait(true);
        EventTestHarness harness = new EventTestHarness(CORRELATION_ID);
        CancellingGroupCreatingBehavior behavior = new CancellingGroupCreatingBehavior();
        harness.RegisterBehavior<GroupCreatingEvent>(behavior);
        FakeWalletGrain wallet = new FakeWalletGrain();
        FakePlayerGrain player = new FakePlayerGrain();
        GroupDirectoryGrain grain = CreateGrain(options, harness, wallet, player);

        int? groupId = await grain
            .CreateGroupAsync(
                new PlayerId(7),
                "Blocked Guild",
                "blocked",
                1,
                2,
                70,
                new List<int> { 1, 2, 3 },
                CancellationToken.None
            )
            .ConfigureAwait(true);

        groupId.Should().BeNull();
        wallet.DebitCalls.Should().Be(0);
        player.TrackCreditSpendCalls.Should().Be(0);
        behavior.CorrelationId.Should().Be(CORRELATION_ID);

        using VortexDbContext db = new VortexDbContext(options);
        (await db.Groups.CountAsync().ConfigureAwait(true)).Should().Be(0);
        (await db.GroupMembers.CountAsync().ConfigureAwait(true)).Should().Be(0);
        (await db.EconomyLedger.CountAsync().ConfigureAwait(true)).Should().Be(0);
        RoomEntity room = await db.Rooms.SingleAsync().ConfigureAwait(true);
        room.GroupEntityId.Should().BeNull();
    }

    [Fact]
    public async Task CreateGroupAsync_NotCancelled_DebitsAndCreatesGroup()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        await SeedOwnerRoomAsync(options, ownerId: 8, roomId: 80).ConfigureAwait(true);
        EventTestHarness harness = new EventTestHarness(CORRELATION_ID);
        CountingGroupCreatedHandler createdHandler = new CountingGroupCreatedHandler();
        harness.RegisterHandler<GroupCreatedEvent>(createdHandler);
        FakeWalletGrain wallet = new FakeWalletGrain();
        FakePlayerGrain player = new FakePlayerGrain();
        GroupDirectoryGrain grain = CreateGrain(options, harness, wallet, player);

        int? groupId = await grain
            .CreateGroupAsync(
                new PlayerId(8),
                "Allowed Guild",
                "allowed",
                3,
                4,
                80,
                new List<int> { 1, 2, 3 },
                CancellationToken.None
            )
            .ConfigureAwait(true);

        groupId.Should().NotBeNull();
        wallet.DebitCalls.Should().Be(1);
        wallet.DebitRequests.Should().ContainSingle();
        wallet.DebitRequests[0].Amount.Should().Be(10);
        wallet.DebitRequests[0].CurrencyKind.CurrencyType.Should().Be(CurrencyType.Credits);
        player.TrackCreditSpendCalls.Should().Be(1);
        createdHandler.Count.Should().Be(1);

        using VortexDbContext db = new VortexDbContext(options);
        GroupEntity group = await db.Groups.SingleAsync().ConfigureAwait(true);
        group.Name.Should().Be("Allowed Guild");
        group.RoomEntityId.Should().Be(80);
        (await db.GroupMembers.CountAsync().ConfigureAwait(true)).Should().Be(1);
        RoomEntity room = await db.Rooms.SingleAsync().ConfigureAwait(true);
        room.GroupEntityId.Should().Be(group.Id);
    }

    private static DbContextOptions<VortexDbContext> NewOptions()
    {
        return new DbContextOptionsBuilder<VortexDbContext>()
            .UseInMemoryDatabase($"group-directory-{Guid.NewGuid():N}")
            // Guild creation wraps its two SaveChanges calls in a transaction so the group row and
            // the room back-link land together. The in-memory provider has no transactions and
            // warns instead; the atomicity this guards is a relational concern, so ignore it here.
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private static async Task SeedOwnerRoomAsync(
        DbContextOptions<VortexDbContext> options,
        int ownerId,
        int roomId
    )
    {
        using VortexDbContext db = new VortexDbContext(options);
        PlayerEntity owner = new PlayerEntity
        {
            Id = ownerId,
            Name = $"Owner{ownerId}",
            Figure = "hr-115",
            Gender = AvatarGenderType.Male,
            PlayerStatus = PlayerStatusType.Offline,
            PlayerPerks = PlayerPerkFlags.None,
        };
        RoomModelEntity model = new RoomModelEntity
        {
            Id = roomId + 1000,
            Name = $"model-{roomId}",
            Model = "0",
            DoorX = 0,
            DoorY = 0,
            DoorRotation = Rotation.North,
            Enabled = true,
            Custom = false,
        };
        RoomEntity room = new RoomEntity
        {
            Id = roomId,
            Name = $"Room{roomId}",
            PlayerEntityId = ownerId,
            DoorMode = RoomDoorModeType.Open,
            RoomModelEntityId = model.Id,
            UsersNow = 0,
            PlayersMax = 25,
            PaintWall = string.Empty,
            PaintFloor = string.Empty,
            PaintLandscape = string.Empty,
            WallHeight = -1,
            HideWalls = false,
            ThicknessWall = RoomThicknessType.Normal,
            ThicknessFloor = RoomThicknessType.Normal,
            AllowBlocking = false,
            AllowPets = false,
            AllowPetsEat = false,
            TradeType = RoomTradeModeType.Disabled,
            MuteType = ModSettingType.Owner,
            KickType = ModSettingType.Owner,
            BanType = ModSettingType.Owner,
            ChatModeType = ChatModeType.FreeFlow,
            ChatBubbleType = ChatBubbleWidthType.Normal,
            ChatSpeedType = ChatScrollSpeedType.Normal,
            ChatFloodType = ChatFloodSensitivityType.Minimal,
            ChatDistance = 50,
            Score = 0,
            PlayerEntity = owner,
            RoomModelEntity = model,
        };

        db.Players.Add(owner);
        db.RoomModels.Add(model);
        db.Rooms.Add(room);
        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    private static GroupDirectoryGrain CreateGrain(
        DbContextOptions<VortexDbContext> options,
        EventTestHarness harness,
        FakeWalletGrain wallet,
        FakePlayerGrain player
    )
    {
        IGrainFactory grainFactory = CreateGrainFactory(wallet, player);

        return new GroupDirectoryGrain(
            new TestDbContextFactory(options),
            grainFactory,
            new NullGroupBadgePartProvider(),
            harness.System,
            harness.System,
            NullLogger<GroupDirectoryGrain>.Instance
        );
    }

    private static IGrainFactory CreateGrainFactory(FakeWalletGrain wallet, FakePlayerGrain player)
    {
        IGrainFactory grainFactory = DispatchProxy.Create<IGrainFactory, GrainFactoryProxy>();
        GrainFactoryProxy proxy = (GrainFactoryProxy)(object)grainFactory;
        proxy.Wallet = wallet;
        proxy.Player = player;

        return grainFactory;
    }

    private sealed class TestDbContextFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext()
        {
            return new VortexDbContext(options);
        }
    }

    private class GrainFactoryProxy : DispatchProxy
    {
        public FakeWalletGrain Wallet { get; set; } = default!;

        public FakePlayerGrain Player { get; set; } = default!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is not null && targetMethod.Name == "GetGrain")
            {
                Type grainType = targetMethod.GetGenericArguments()[0];

                if (grainType == typeof(IPlayerWalletGrain))
                {
                    return Wallet;
                }

                if (grainType == typeof(IPlayerGrain))
                {
                    return Player;
                }

                if (grainType == typeof(IServerConfigGrain))
                {
                    return new FakeServerConfigGrain();
                }
            }

            throw new NotSupportedException(targetMethod?.Name);
        }
    }

    private sealed class FakeServerConfigGrain : IServerConfigGrain
    {
        // Returns the caller's fallback, so every migrated group knob resolves to its in-code
        // default (creation cost = 10) — preserving the pre-migration IOptions defaults.
        public Task<int> GetIntAsync(string key, int fallback) => Task.FromResult(fallback);

        public Task<string?> GetValueAsync(string key) => Task.FromResult<string?>(null);

        public Task<bool> GetBoolAsync(string key, bool fallback) => Task.FromResult(fallback);

        public Task<double> GetDoubleAsync(string key, double fallback) =>
            Task.FromResult(fallback);

        public Task<ImmutableDictionary<string, string>> GetAllAsync() =>
            Task.FromResult(ImmutableDictionary<string, string>.Empty);

        public Task<ImmutableDictionary<string, string>> GetManyAsync(
            ImmutableArray<string> keys
        ) => Task.FromResult(ImmutableDictionary<string, string>.Empty);

        public Task SetValueAsync(string key, string value, string? description) =>
            Task.CompletedTask;

        public Task ReloadAsync() => Task.CompletedTask;

        public Task<ImmutableArray<string>> GetMotdAsync() =>
            Task.FromResult(ImmutableArray<string>.Empty);

        public Task SetMotdAsync(ImmutableArray<string> lines) => Task.CompletedTask;
    }

    private sealed class FakeWalletGrain : IPlayerWalletGrain
    {
        public int DebitCalls { get; private set; }

        public List<WalletDebitRequest> DebitRequests { get; } = new List<WalletDebitRequest>();

        public Task<WalletDebitResult> TryDebitAsync(
            List<WalletDebitRequest> requests,
            CancellationToken ct
        )
        {
            DebitCalls++;
            DebitRequests.AddRange(requests);

            return Task.FromResult(WalletDebitResult.Success());
        }

        public Task<WalletDebitResult> TryDebitAsync(
            List<WalletDebitRequest> requests,
            CommerceOperationId operationId,
            CancellationToken ct
        ) => TryDebitAsync(requests, ct);

        public Task<bool> CreditOnceAsync(
            List<WalletDebitRequest> credits,
            CommerceOperationId operationId,
            string stepKey,
            CancellationToken ct
        ) => CreditBackAsync(credits, ct).ContinueWith(_ => true, TaskScheduler.Default);

        public Task CreditBackAsync(List<WalletDebitRequest> requests, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task CreditBackAsync(
            List<WalletDebitRequest> requests,
            CommerceOperationId operationId,
            CancellationToken ct
        ) => CreditBackAsync(requests, ct);

        public Task<int> GetAmountForCurrencyAsync(CurrencyKind kind, CancellationToken ct)
        {
            return Task.FromResult(0);
        }

        public Task<Dictionary<int, int>> GetActivityPointsAsync(CancellationToken ct)
        {
            return Task.FromResult(new Dictionary<int, int>());
        }

        public Task GrantCreditsAsync(int amount, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task<bool> GrantCurrencyAsync(CurrencyKind kind, int amount, CancellationToken ct)
        {
            return Task.FromResult(true);
        }

        public Task GrantActivityPointsAsync(
            int activityPointType,
            int amount,
            CancellationToken ct
        )
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakePlayerGrain : IPlayerGrain
    {
        // Chat word filters arrived on IPlayerGrain after this fake was written. Nothing in these
        // tests touches them; they are here so the project compiles.
        public Task<ImmutableArray<string>> GetWordFilterAsync(CancellationToken ct) =>
            Task.FromResult(ImmutableArray<string>.Empty);

        public Task<bool> AddWordFilterAsync(string word, CancellationToken ct) =>
            Task.FromResult(false);

        public Task<bool> RemoveWordFilterAsync(string word, CancellationToken ct) =>
            Task.FromResult(false);

        // Same story for the mod-tool surface: unused here, present so the project compiles.
        public Task<PlayerModeratorInfoSnapshot> GetModeratorInfoAsync(CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<bool> MarkLoggedInAsync(CancellationToken ct) => Task.FromResult(false);

        public Task<PlayerModToolPreferencesSnapshot> GetModToolPreferencesAsync(
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task SetModToolPreferencesAsync(
            PlayerModToolPreferencesSnapshot preferences,
            CancellationToken ct
        ) => Task.CompletedTask;

        public int TrackCreditSpendCalls { get; private set; }

        public Task TrackCreditSpendAsync(int credits, CancellationToken ct)
        {
            TrackCreditSpendCalls++;

            return Task.CompletedTask;
        }

        public int FavouriteGroupId { get; private set; }

        public Task SetFavouriteGroupAsync(int groupId, CancellationToken ct)
        {
            FavouriteGroupId = groupId;

            return Task.CompletedTask;
        }

        public Task SetFigureAsync(string figure, AvatarGenderType gender, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task SetNameAsync(string name, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task<bool> IsNuxCompletedAsync(CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task MarkNuxCompletedAsync(CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task<bool> IsSafetyLockedAsync(CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task OnAccountSafetyLockChangedAsync(bool locked, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task SetMottoAsync(string text, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task<int> AddAchievementScoreAsync(int delta, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task<PlayerSummarySnapshot> GetSummaryAsync(CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task<PlayerExtendedProfileSnapshot> GetExtendedProfileSnapshotAsync(
            PlayerId viewerId,
            CancellationToken ct
        )
        {
            throw new NotSupportedException();
        }

        public Task<ClubSubscriptionSnapshot> GetClubSubscriptionAsync(CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task<ClubPurchaseResult> PurchaseClubAsync(
            int months,
            bool isVip,
            int costCredits,
            CancellationToken ct
        )
        {
            throw new NotSupportedException();
        }

        public Task<ClubPurchaseResult> GrantClubMonthsAsync(
            int months,
            bool isVip,
            CommerceOperationId operation,
            CancellationToken ct
        )
        {
            throw new NotSupportedException();
        }

        public Task<bool> TryConsumeClubGiftAsync(string productCode, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task<bool> GrantPerkAsync(PlayerPerkFlags perk, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task<PlayerPerkFlags> GetPerksAsync(CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task<bool> ApplyAccountBanAsync(
            int actorPlayerId,
            DateTime? bannedUntil,
            string reason,
            CancellationToken ct
        )
        {
            throw new NotSupportedException();
        }

        public Task<DateTime?> ApplyHotelMuteAsync(
            int actorPlayerId,
            DateTime? mutedUntil,
            CancellationToken ct
        )
        {
            throw new NotSupportedException();
        }

        public Task<bool> ApplyTradingLockAsync(
            int actorPlayerId,
            DateTime? lockedUntil,
            CancellationToken ct
        )
        {
            throw new NotSupportedException();
        }

        public Task<DateTime?> GetActiveBanExpiryAsync(CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task<PlayerAccountPreferencesSnapshot> GetAccountPreferencesAsync(
            CancellationToken ct
        )
        {
            throw new NotSupportedException();
        }

        public Task SetSoundSettingsAsync(
            int uiVolume,
            int furniVolume,
            int traxVolume,
            CancellationToken ct
        )
        {
            throw new NotSupportedException();
        }

        public Task SetFreeFlowChatDisabledAsync(bool disabled, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task SetChatDisplayPreferencesAsync(
            int chatMode,
            int bubbleWidth,
            int scrollSpeed,
            CancellationToken ct
        )
        {
            throw new NotSupportedException();
        }

        public Task SetChatSizePreferenceAsync(int sizePreference, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task SetRoomInvitesIgnoredAsync(bool ignored, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task SetRoomCameraFollowDisabledAsync(bool disabled, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task SetUiFlagsAsync(int flags, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task SetDiscordPreferencesAsync(
            int version,
            bool showHabbo,
            bool shareActivity,
            bool hideInHiddenRooms,
            bool allowJoining,
            CancellationToken ct
        )
        {
            throw new NotSupportedException();
        }

        public Task<List<PlayerWardrobeOutfitSnapshot>> GetWardrobeAsync(CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task SaveWardrobeOutfitAsync(
            int slotId,
            string figure,
            string gender,
            CancellationToken ct
        )
        {
            throw new NotSupportedException();
        }
    }

    private sealed class CancellingGroupCreatingBehavior : IEventBehavior<GroupCreatingEvent>
    {
        public string? CorrelationId { get; private set; }

        public ValueTask InvokeAsync(
            GroupCreatingEvent env,
            EventContext ctx,
            Func<ValueTask> next,
            CancellationToken ct
        )
        {
            CorrelationId = ctx.CorrelationId;
            ctx.Cancel = true;
            ctx.CancelReason = "blocked";

            return ValueTask.CompletedTask;
        }
    }

    private sealed class CountingGroupCreatedHandler : IEventHandler<GroupCreatedEvent>
    {
        public int Count { get; private set; }

        public ValueTask HandleAsync(GroupCreatedEvent env, EventContext ctx, CancellationToken ct)
        {
            Count++;

            return ValueTask.CompletedTask;
        }
    }
}
