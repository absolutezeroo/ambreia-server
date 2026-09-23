using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Entities.Wired;
using Vortex.Furniture.Providers;
using Vortex.Primitives.Action;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Events;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Furniture.Snapshots;
using Vortex.Primitives.Furniture.StuffData;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Pets.Providers;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Players.Wallet;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Providers;
using Vortex.Primitives.Rooms.Snapshots.Wired;
using Vortex.Primitives.Sound.Providers;
using Vortex.Protocol.Messages.Outgoing.Userdefinedroomevents.Wiredtrading;
using Vortex.Rooms.Configuration;
using Vortex.Rooms.Grains;
using Vortex.Rooms.Tests.Support;
using Vortex.Rooms.Wired.Logs;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Wired;

/// <summary>
/// The money legs of a contract settlement, which is the one path here that can take from a wallet
/// and write to a chest in the same breath.
/// </summary>
/// <remarks>
/// Every case is a case where getting the order wrong costs somebody real credits: the payment is
/// taken before anything moves, the reward is handed over after, and the log is written from what
/// landed rather than from what was meant to.
/// <para>
/// Coins on both sides deliberately — furniture would only add rows to assert about, and the
/// matching itself is already pinned by <see cref="WiredContractSettlementTests" />.
/// </para>
/// </remarks>
public sealed class WiredContractSettlementOrderingTests
{
    private const int ROOM_ID = 55;
    private const int CHEST_ID = 900;
    private const int CONTRACT_ID = 901;
    private const int PLAYER_ID = 11;
    private const int PAYMENT = 50;
    private const int REWARD = 20;
    private const int CHEST_CREDITS = 100;

    [Fact]
    public async Task GoodsThatCannotBeSaved_GiveThePaymentBack()
    {
        Harness harness = new(seedChest: true, failOnSaveNumber: 1);

        await harness.OfferAndAcceptAsync().ConfigureAwait(true);

        // The debit happens before the write, so a write that fails leaves the player having paid
        // for a trade that never happened — and the screen open to be accepted again.
        harness.Wallet.Debited.Should().Equal(PAYMENT);
        harness.Wallet.Granted.Should().Equal(PAYMENT);
    }

    [Fact]
    public async Task ARewardTheWalletRefuses_IsNotLoggedAsPaid()
    {
        Harness harness = new(seedChest: true);
        harness.Wallet.GrantSucceeds = false;

        await harness.OfferAndAcceptAsync().ConfigureAwait(true);

        // The chest keeps what it could not hand over: the payment went in, the reward never left.
        harness.ChestCredits.Should().Be(CHEST_CREDITS + PAYMENT);

        WiredChestTransactionEntity row = harness.LedgerRows.Should().ContainSingle().Subject;

        row.DepositCoinsCount.Should().Be(PAYMENT);
        row.WithdrawCoinsCount.Should().Be(0);
    }

    [Fact]
    public async Task ARewardThatLands_IsLoggedAsPaid()
    {
        Harness harness = new(seedChest: true);

        await harness.OfferAndAcceptAsync().ConfigureAwait(true);

        harness.ChestCredits.Should().Be(CHEST_CREDITS + PAYMENT - REWARD);
        harness.Wallet.Granted.Should().Equal(REWARD);

        WiredChestTransactionEntity row = harness.LedgerRows.Should().ContainSingle().Subject;

        row.DepositCoinsCount.Should().Be(PAYMENT);
        row.WithdrawCoinsCount.Should().Be(REWARD);
    }

    /// <summary>
    /// ECON-CHEST-015: the settlement debited a wallet, moved furniture in both directions and
    /// adjusted a chest's credits with nothing durable naming the operation. Its own books are
    /// written from what landed, which is a receipt for what happened and no record at all of what
    /// stopped halfway.
    /// </summary>
    [Fact]
    public async Task ASettlementThatGoesThrough_IsOpenedPivotedAndCompleted()
    {
        Harness harness = new(seedChest: true);

        await harness.OfferAndAcceptAsync().ConfigureAwait(true);

        harness.JournalOpened.Should().Equal(CommerceOperationKind.WiredChestContract);
        harness
            .JournalStates.Should()
            .Equal(
                CommerceOperationState.Debited,
                CommerceOperationState.Pivoted,
                CommerceOperationState.Completed
            );
    }

    [Fact]
    public async Task ASettlementCompensatedBeforeThePivot_SaysSo()
    {
        Harness harness = new(seedChest: true, failOnSaveNumber: 1);

        await harness.OfferAndAcceptAsync().ConfigureAwait(true);

        // The refund landed, so the operation is closed as compensated rather than left at Debited
        // -- which is what an operation somebody still has to finish looks like.
        harness.Wallet.Granted.Should().Equal(PAYMENT);
        harness
            .JournalStates.Should()
            .Equal(CommerceOperationState.Debited, CommerceOperationState.FailedBeforePivot);
    }

    [Fact]
    public async Task ASettlementThatFailsAfterThePivot_GoesOnTheOperatorsList()
    {
        // The goods have moved by the second save, so there is nothing to compensate and the coins
        // are not given back: it is owed, and an operator settles it.
        Harness harness = new(seedChest: true, failOnSaveNumber: 2);

        await harness.OfferAndAcceptAsync().ConfigureAwait(true);

        harness.Wallet.Granted.Should().NotContain(PAYMENT);
        harness.JournalStates.Should().EndWith(CommerceOperationState.NeedsIntervention);
    }

    [Fact]
    public async Task AFurniThatIsNotAChest_SettlesNothing()
    {
        // The box's furni picker takes the contract and the chest from one list, so the id the
        // offer carries is as often the contract as it is the shop.
        Harness harness = new(seedChest: false, chestId: CONTRACT_ID);

        await harness.OfferAndAcceptAsync().ConfigureAwait(true);

        // It used to open a chest row keyed to the contract furni and pay the stake into it.
        harness.ChestRowCount.Should().Be(0);
        harness.Wallet.Debited.Should().BeEmpty();
    }

    [Fact]
    public async Task ALockedChest_HandsNothingOver()
    {
        Harness harness = new(seedChest: true, locked: true);

        await harness.OfferAndAcceptAsync().ConfigureAwait(true);

        harness.ChestCredits.Should().Be(CHEST_CREDITS);
        harness.Wallet.Debited.Should().BeEmpty();
        harness.LedgerRows.Should().BeEmpty();
    }

    private sealed class Harness
    {
        private readonly DbContextOptions<VortexDbContext> _options;

        private readonly int _chestId;

        public Harness(
            bool seedChest,
            int failOnSaveNumber = 0,
            int chestId = CHEST_ID,
            bool locked = false
        )
        {
            _chestId = chestId;

            _options = new DbContextOptionsBuilder<VortexDbContext>()
                .UseInMemoryDatabase($"wired-settlement-{Guid.NewGuid()}")
                .Options;

            if (seedChest)
            {
                using VortexDbContext db = new(_options);

                db.WiredChests.Add(
                    new WiredChestEntity
                    {
                        FurnitureEntityId = CHEST_ID,
                        Credits = CHEST_CREDITS,
                        NotificationsEnabled = false,
                        Locked = locked,
                    }
                );

                db.SaveChanges();
            }

            Grain = GrainActivationContext.CreateWithIntegerKey<RoomGrain>(
                ROOM_ID,
                new FaultyDbContextFactory(_options, failOnSaveNumber),
                FakeProxy.Create<IFurnitureDefinitionProvider>(_ => null),
                new StuffDataFactory(),
                Options.Create(new RoomConfig()),
                NullLogger<IRoomGrain>.Instance,
                FakeProxy.Create<IRoomModelProvider>(_ => null),
                FakeProxy.Create<IRoomItemsProvider>(_ => null),
                FakeProxy.Create<IRoomObjectLogicProvider>(_ => null),
                FakeProxy.Create<IRoomAvatarProvider>(_ => null),
                FakeProxy.Create<IRoomWiredVariablesProvider>(_ => null),
                RoomGrainStubs.NoListeners(),
                BuildGrainFactory(),
                FakeProxy.Create<IEventPublisher>(_ => null),
                RoomGrainStubs.NeverCancels(),
                FakeProxy.Create<IPermissionService>(_ => null),
                FakeProxy.Create<IVortexMetrics>(_ => null),
                FakeProxy.Create<IRoomModerationStore>(_ => null),
                FakeProxy.Create<IPetLevelProvider>(_ => null),
                FakeProxy.Create<IPetCommandProvider>(_ => null),
                FakeProxy.Create<IPetVocalProvider>(_ => null),
                new RoomWiredLogChannel(),
                FakeProxy.Create<ISongProvider>(_ => null),
                TestRoomGames.Provider(),
                BuildJournal(),
                RoomGrainStubs.PassThroughWordFilter()
            );

            Grain._state.ItemsById[CHEST_ID] = ItemWithLogic("furniture_furnichest");
            Grain._state.ItemsById[CONTRACT_ID] = ItemWithLogic("wf_contract_trade");
        }

        public RoomGrain Grain { get; }

        public RecordingWallet Wallet { get; } = new();

        public int ChestCredits
        {
            get
            {
                using VortexDbContext db = new(_options);

                return db.WiredChests.Single(chest => chest.FurnitureEntityId == CHEST_ID).Credits;
            }
        }

        public int ChestRowCount
        {
            get
            {
                using VortexDbContext db = new(_options);

                return db.WiredChests.Count();
            }
        }

        public List<WiredChestTransactionEntity> LedgerRows
        {
            get
            {
                using VortexDbContext db = new(_options);

                return [.. db.WiredChestTransactions];
            }
        }

        /// <summary>The whole public route: a box offers the contract, the player confirms.</summary>
        public async Task OfferAndAcceptAsync()
        {
            TradeContract contract = new()
            {
                YouGiveRules = [Rule(PAYMENT)],
                YouGetRule = Rule(REWARD),
                Mode = 0,
                Multiplier = 1,
                AutoMultiplierMax = 1,
            };

            await Grain
                .OfferTransactionAsync(
                    CONTRACT_ID,
                    new PlayerId(PLAYER_ID),
                    _chestId,
                    contract,
                    mode: 0,
                    multiplier: 1,
                    timeoutSeconds: 0,
                    CancellationToken.None
                )
                .ConfigureAwait(true);

            await Grain
                .AcceptWiredDepositAsync(
                    new ActionContext(
                        ActionOrigin.Player,
                        default,
                        new PlayerId(PLAYER_ID),
                        new RoomId(ROOM_ID)
                    ),
                    confirm: true,
                    CancellationToken.None
                )
                .ConfigureAwait(true);
        }

        private static TradeContractRule Rule(int coins) =>
            new() { Nodes = [new TradeContractNode { IsFurni = false, Amount = coins }] };

        private static IRoomItem ItemWithLogic(string logicName)
        {
            FurnitureDefinitionSnapshot definition = new()
            {
                Id = 1,
                SpriteId = 1,
                Name = logicName,
                ProductType = ProductType.Floor,
                FurniCategory = FurnitureCategory.Default,
                LogicName = logicName,
                TotalStates = 1,
                Width = 1,
                Length = 1,
                StackHeight = Altitude.Zero,
                CanStack = false,
                CanWalk = false,
                CanSit = false,
                CanLay = false,
                CanRecycle = false,
                CanTrade = false,
                CanGroup = false,
                CanSell = false,
                UsagePolicy = FurnitureUsageType.Nobody,
                ExtraData = null,
                StuffDataType = StuffDataType.LegacyKey,
            };

            return FakeProxy.Create<IRoomItem>(call =>
                call.Method.Name == $"get_{nameof(IRoomItem.Definition)}" ? definition : null
            );
        }

        /// <summary>Every state the settlement moved its operation through, in order.</summary>
        public List<CommerceOperationState> JournalStates { get; } = [];

        /// <summary>The kinds it opened. One per settlement that got past preflight.</summary>
        public List<CommerceOperationKind> JournalOpened { get; } = [];

        private ICommerceJournal BuildJournal() =>
            FakeProxy.Create<ICommerceJournal>(call =>
            {
                switch (call.Method.Name)
                {
                    case nameof(ICommerceJournal.OpenAsync):
                        JournalOpened.Add((CommerceOperationKind)call.Args![1]!);
                        break;
                    case nameof(ICommerceJournal.TransitionAsync):
                        JournalStates.Add((CommerceOperationState)call.Args![1]!);
                        break;
                }

                return Task.CompletedTask;
            });

        private IGrainFactory BuildGrainFactory() =>
            FakeProxy.Create<IGrainFactory>(call =>
            {
                if (!call.Method.IsGenericMethod)
                {
                    return null;
                }

                Type grain = call.Method.GetGenericArguments()[0];

                if (grain == typeof(IPlayerWalletGrain))
                {
                    return Wallet.AsGrain();
                }

                // The offer and the announcement both push a composer at the player, and neither is
                // what these tests are about.
                return grain == typeof(IPlayerPresenceGrain)
                    ? FakeProxy.Create<IPlayerPresenceGrain>(_ => Task.CompletedTask)
                    : null;
            });
    }

    /// <summary>What the wallet was asked for, and what it was willing to do about it.</summary>
    public sealed class RecordingWallet
    {
        public bool DebitSucceeds { get; set; } = true;

        public bool GrantSucceeds { get; set; } = true;

        public List<int> Debited { get; } = [];

        /// <summary>Rewards and refunds both land here — the point of a refund is that it is an
        /// ordinary grant, and a test that could not confuse the two would not catch the bug.</summary>
        public List<int> Granted { get; } = [];

        public IPlayerWalletGrain AsGrain() =>
            FakeProxy.Create<IPlayerWalletGrain>(call =>
            {
                switch (call.Method.Name)
                {
                    case nameof(IPlayerWalletGrain.TryDebitAsync):
                        if (!DebitSucceeds)
                        {
                            return Task.FromResult(
                                WalletDebitResult.InsufficientBalance(
                                    new WalletDebitFailure
                                    {
                                        CurrencyKind = new CurrencyKind
                                        {
                                            CurrencyType = CurrencyType.Credits,
                                        },
                                        Amount = PAYMENT,
                                    }
                                )
                            );
                        }

                        foreach (
                            WalletDebitRequest request in (List<WalletDebitRequest>)call.Args![0]!
                        )
                        {
                            Debited.Add(request.Amount);
                        }

                        return Task.FromResult(WalletDebitResult.Success());

                    case nameof(IPlayerWalletGrain.GrantCurrencyAsync):
                        if (GrantSucceeds)
                        {
                            Granted.Add((int)call.Args![1]!);
                        }

                        return Task.FromResult(GrantSucceeds);

                    default:
                        return null;
                }
            });
    }

    /// <summary>
    /// A context factory whose n-th save throws, which is the only way to stand in the one gap the
    /// settlement cannot close: the wallet has been debited and the goods have not moved yet.
    /// </summary>
    private sealed class FaultyDbContextFactory(
        DbContextOptions<VortexDbContext> options,
        int failOnSaveNumber
    ) : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new FaultyDbContext(options, failOnSaveNumber);
    }

    private sealed class FaultyDbContext(
        DbContextOptions<VortexDbContext> options,
        int failOnSaveNumber
    ) : VortexDbContext(options)
    {
        private int _saves;

        public override Task<int> SaveChangesAsync(CancellationToken ct = default) =>
            ++_saves == failOnSaveNumber
                ? throw new InvalidOperationException("save refused by the test")
                : base.SaveChangesAsync(ct);
    }
}
