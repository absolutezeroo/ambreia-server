using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans;
using Vortex.Collectibles.Grains;
using Vortex.Collectibles.Vault;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Events;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Players.Wallet;
using Vortex.Primitives.Server.Grains;
using Vortex.Primitives.Vault;
using Vortex.Primitives.Vault.Enums;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Players.Tests.Vault;

/// <summary>
/// The Earnings window pays real currency, so what these pin is that nothing is paid twice: not a
/// deposit replayed, not a claim retried after a crash, not a key whose row was already cashed.
/// </summary>
public sealed class PlayerVaultGrainTests
{
    private const int PlayerId = 7;

    private static IncomeEarning Credits(
        VaultRewardCategoryType category,
        int amount,
        string key
    ) =>
        new()
        {
            Category = category,
            Currency = new CurrencyKind { CurrencyType = CurrencyType.Credits },
            Amount = amount,
            SourceKey = key,
        };

    [Fact]
    public async Task ADeposit_IsARowAndOnePing()
    {
        Harness h = await Harness.StartAsync();

        await h.Grain.EarnAsync(
            Credits(VaultRewardCategoryType.HabboClub, 40, "club-payday:2026-09-01"),
            default
        );

        (await h.Grain.GetIncomeRewardsAsync(default))
            .Should()
            .ContainSingle()
            .Which.Amount.Should()
            .Be(40);
        h.Published.OfType<VaultIncomeDepositedEvent>()
            .Should()
            .ContainSingle()
            .Which.Category.Should()
            .Be(VaultRewardCategoryType.HabboClub);
        h.WalletCredits.Should().BeEmpty();
    }

    [Fact]
    public async Task TheSameSourceKeyTwice_IsOneRowAndOnePing()
    {
        Harness h = await Harness.StartAsync();
        IncomeEarning payday = Credits(
            VaultRewardCategoryType.HabboClub,
            40,
            "club-payday:2026-09-01"
        );

        await h.Grain.EarnAsync(payday, default);
        await h.Grain.EarnAsync(payday, default);

        (await h.Grain.GetIncomeRewardsAsync(default)).Should().ContainSingle();
        h.Published.OfType<VaultIncomeDepositedEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task ADepositReplayedAfterItsClaim_IsNotPaidAgain()
    {
        Harness h = await Harness.StartAsync();
        IncomeEarning payday = Credits(
            VaultRewardCategoryType.HabboClub,
            40,
            "club-payday:2026-09-01"
        );

        await h.Grain.EarnAsync(payday, default);
        (await h.Grain.ClaimAsync(VaultRewardCategoryType.HabboClub, default)).Should().BeTrue();
        await h.Grain.EarnAsync(payday, default);

        (await h.Grain.GetIncomeRewardsAsync(default)).Should().BeEmpty();
        h.WalletCredits.Should().ContainSingle();
    }

    [Fact]
    public async Task ANonVaultCurrency_IsPaidDirectlyOnce()
    {
        Harness h = await Harness.StartAsync();
        IncomeEarning diamonds = new()
        {
            Category = VaultRewardCategoryType.Achievements,
            Currency = new CurrencyKind
            {
                CurrencyType = CurrencyType.ActivityPoints,
                ActivityPointType = 5,
            },
            Amount = 3,
            SourceKey = "achievement:12:level:2",
        };

        await h.Grain.EarnAsync(diamonds, default);
        await h.Grain.EarnAsync(diamonds, default);

        (await h.Grain.GetIncomeRewardsAsync(default)).Should().BeEmpty();
        h.AppliedCredits.Should().ContainSingle(); // second call hit the same (operation, step) receipt
        h.Published.OfType<VaultIncomeDepositedEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task ACategoryRoutedDirect_SkipsTheVault_AndPendingRowsStayClaimable()
    {
        Harness h = await Harness.StartAsync();
        await h.Grain.EarnAsync(
            Credits(VaultRewardCategoryType.HabboClub, 40, "club-payday:2026-08-01"),
            default
        );

        h.Config["earnings.habbo_club.to_vault"] = false;
        await h.Grain.EarnAsync(
            Credits(VaultRewardCategoryType.HabboClub, 50, "club-payday:2026-09-01"),
            default
        );

        (await h.Grain.GetIncomeRewardsAsync(default))
            .Should()
            .ContainSingle()
            .Which.Amount.Should()
            .Be(40);
        h.AppliedCredits.Should().ContainSingle().Which.Amount.Should().Be(50);
        h.Published.OfType<VaultIncomeDepositedEvent>().Should().ContainSingle();

        (await h.Grain.ClaimAsync(VaultRewardCategoryType.HabboClub, default)).Should().BeTrue();
        h.AppliedCredits.Should().HaveCount(2);
    }

    [Fact]
    public async Task ClaimAll_PaysEveryCurrencyRow_AndLeavesAProductRow()
    {
        Harness h = await Harness.StartAsync(seedProductRow: true);
        await h.Grain.EarnAsync(
            Credits(VaultRewardCategoryType.HabboClub, 40, "club-payday:2026-09-01"),
            default
        );
        await h.Grain.EarnAsync(
            Credits(VaultRewardCategoryType.Achievements, 5, "achievement:1:level:1"),
            default
        );

        (await h.Grain.ClaimAsync(VaultRewardCategoryType.All, default)).Should().BeTrue();

        h.AppliedCredits.Select(c => c.Amount).Should().BeEquivalentTo([40, 5]);
        (await h.Grain.GetIncomeRewardsAsync(default))
            .Should()
            .ContainSingle()
            .Which.RewardType.Should()
            .Be(VaultRewardType.Product);
        h.Published.OfType<VaultIncomeClaimedEvent>()
            .Should()
            .ContainSingle()
            .Which.Rewards.Should()
            .Be(2);
    }

    [Fact]
    public async Task OneRowFailingToPay_DoesNotHoldBackTheOthers()
    {
        Harness h = await Harness.StartAsync();
        await h.Grain.EarnAsync(
            Credits(VaultRewardCategoryType.HabboClub, 40, "club-payday:2026-08-01"),
            default
        );
        await h.Grain.EarnAsync(
            Credits(VaultRewardCategoryType.HabboClub, 5, "club-payday:2026-09-01"),
            default
        );

        h.FailNextCredit = true; // the first row's credit is refused, nothing recorded
        (await h.Grain.ClaimAsync(VaultRewardCategoryType.HabboClub, default)).Should().BeFalse();

        h.AppliedCredits.Should().ContainSingle().Which.Amount.Should().Be(5);
        (await h.Grain.GetIncomeRewardsAsync(default))
            .Should()
            .ContainSingle()
            .Which.Amount.Should()
            .Be(40);
        (await h.ReadRowsAsync()).Single(r => r.Amount == 5).ClaimedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task AKeyDepositedThenRetriedAfterAFlipToDirect_IsNotPaidDirect()
    {
        Harness h = await Harness.StartAsync();
        IncomeEarning payday = Credits(
            VaultRewardCategoryType.HabboClub,
            40,
            "club-payday:2026-09-01"
        );

        await h.Grain.EarnAsync(payday, default);
        h.Config["earnings.habbo_club.to_vault"] = false;
        await h.Grain.EarnAsync(payday, default);

        h.AppliedCredits.Should().BeEmpty();
        (await h.ReadRowsAsync()).Should().ContainSingle().Which.ClaimedAt.Should().BeNull();

        (await h.Grain.ClaimAsync(VaultRewardCategoryType.HabboClub, default)).Should().BeTrue();
        h.AppliedCredits.Should().ContainSingle();
    }

    [Fact]
    public async Task AKeyPaidDirectThenRetriedAfterAFlipToVault_IsNotDepositedAgain()
    {
        Harness h = await Harness.StartAsync();
        IncomeEarning payday = Credits(
            VaultRewardCategoryType.HabboClub,
            40,
            "club-payday:2026-09-01"
        );

        h.Config["earnings.habbo_club.to_vault"] = false;
        await h.Grain.EarnAsync(payday, default);
        h.AppliedCredits.Should().ContainSingle();

        h.Config["earnings.habbo_club.to_vault"] = true;
        await h.Grain.EarnAsync(payday, default);

        (await h.Grain.GetIncomeRewardsAsync(default)).Should().BeEmpty();
        h.Published.OfType<VaultIncomeDepositedEvent>().Should().BeEmpty();
        h.AppliedCredits.Should().ContainSingle();
    }

    [Fact]
    public async Task ADirectEarning_LeavesAClaimedRowThatSpendsTheKey()
    {
        Harness h = await Harness.StartAsync();
        h.Config["earnings.habbo_club.to_vault"] = false;

        await h.Grain.EarnAsync(
            Credits(VaultRewardCategoryType.HabboClub, 40, "club-payday:2026-09-01"),
            default
        );

        PlayerVaultIncomeRewardEntity row = (await h.ReadRowsAsync())
            .Should()
            .ContainSingle()
            .Subject;
        row.SourceKey.Should().Be("club-payday:2026-09-01");
        row.ClaimedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task AnInsertThatFailsForAnotherReason_Throws()
    {
        Harness h = await Harness.StartAsync();
        h.FailingInserts.Enabled = true;

        await h
            .Grain.Invoking(g =>
                g.EarnAsync(
                    Credits(VaultRewardCategoryType.HabboClub, 40, "club-payday:2026-09-01"),
                    default
                )
            )
            .Should()
            .ThrowAsync<DbUpdateException>();

        (await h.ReadRowsAsync()).Should().BeEmpty();
        (await h.Grain.GetIncomeRewardsAsync(default)).Should().BeEmpty();
        h.Published.OfType<VaultIncomeDepositedEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task AClaimThatCrashedAfterTheCredit_IsFinishedByTheNextClaim_WithoutPayingTwice()
    {
        Harness h = await Harness.StartAsync();
        await h.Grain.EarnAsync(
            Credits(VaultRewardCategoryType.HabboClub, 40, "club-payday:2026-09-01"),
            default
        );

        h.ThrowAfterNextCredit = true; // wallet commits the receipt, then the call throws
        bool first = await h.Grain.ClaimAsync(VaultRewardCategoryType.HabboClub, default);
        first.Should().BeFalse();
        (await h.Grain.GetIncomeRewardsAsync(default)).Should().ContainSingle();

        (await h.Grain.ClaimAsync(VaultRewardCategoryType.HabboClub, default)).Should().BeTrue();

        h.AppliedCredits.Should().ContainSingle();
        (await h.Grain.GetIncomeRewardsAsync(default)).Should().BeEmpty();
    }

    [Fact]
    public async Task AClaimedRow_IsMarked_NotDeleted()
    {
        Harness h = await Harness.StartAsync();
        await h.Grain.EarnAsync(
            Credits(VaultRewardCategoryType.HabboClub, 40, "club-payday:2026-09-01"),
            default
        );

        await h.Grain.ClaimAsync(VaultRewardCategoryType.HabboClub, default);

        (await h.ReadRowsAsync()).Should().ContainSingle().Which.ClaimedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task NothingToClaim_IsFalse()
    {
        Harness h = await Harness.StartAsync();

        (await h.Grain.ClaimAsync(VaultRewardCategoryType.Marketplace, default)).Should().BeFalse();
        (await h.Grain.ClaimAsync((VaultRewardCategoryType)99, default)).Should().BeFalse();
    }

    private sealed class Harness
    {
        private readonly DbContextOptions<VortexDbContext> _options;
        private readonly HashSet<(CommerceOperationId, string)> _receipts = [];

        private Harness(
            DbContextOptions<VortexDbContext> options,
            FailingInsertInterceptor failingInserts
        )
        {
            _options = options;
            FailingInserts = failingInserts;
        }

        public PlayerVaultGrain Grain { get; private set; } = null!;

        public List<IEvent> Published { get; } = [];

        public Dictionary<string, bool> Config { get; } = [];

        /// <summary>Every <c>CreditOnceAsync</c> call, applied or not.</summary>
        public List<ProxyCall> WalletCredits { get; } = [];

        /// <summary>Only the credits whose (operation, step) receipt did not exist yet.</summary>
        public List<WalletDebitRequest> AppliedCredits { get; } = [];

        /// <summary>The next credit commits its receipt, then the call throws, like a lost reply.</summary>
        public bool ThrowAfterNextCredit { get; set; }

        /// <summary>The next credit is refused outright: no receipt, nothing applied.</summary>
        public bool FailNextCredit { get; set; }

        /// <summary>Makes an Earnings insert fail with something other than a duplicate key.</summary>
        public FailingInsertInterceptor FailingInserts { get; }

        public async Task<List<PlayerVaultIncomeRewardEntity>> ReadRowsAsync()
        {
            await using VortexDbContext dbCtx = new(_options);

            return await dbCtx.PlayerVaultIncomeRewards.AsNoTracking().ToListAsync();
        }

        public static async Task<Harness> StartAsync(bool seedProductRow = false)
        {
            FailingInsertInterceptor failingInserts = new();
            DbContextOptions<VortexDbContext> options =
                new DbContextOptionsBuilder<VortexDbContext>()
                    .UseInMemoryDatabase($"vault-{Guid.NewGuid():N}")
                    .AddInterceptors(failingInserts)
                    .Options;

            if (seedProductRow)
            {
                await using VortexDbContext seed = new(options);

                seed.PlayerVaultIncomeRewards.Add(
                    new PlayerVaultIncomeRewardEntity
                    {
                        PlayerEntityId = PlayerId,
                        RewardCategory = (int)VaultRewardCategoryType.BonusBag,
                        RewardType = (int)VaultRewardType.Product,
                        Amount = 1,
                        ProductCode = "bonus_bag_prize",
                        SourceKey = "bonus-bag:1",
                    }
                );

                await seed.SaveChangesAsync();
            }

            Harness h = new(options, failingInserts);
            IGrainFactory grainFactory = h.BuildGrainFactory();

            h.Grain = GrainActivationContext.CreateWithIntegerKey<PlayerVaultGrain>(
                PlayerId,
                new InMemoryDbContextFactory(options),
                grainFactory,
                FakeProxy.Create<IEventPublisher>(call =>
                {
                    if (call.Args?[0] is IEvent raised)
                    {
                        h.Published.Add(raised);
                    }

                    return Task.CompletedTask;
                }),
                new VaultRewardSettlerMap([new CurrencyVaultRewardSettler(grainFactory)]),
                NullLogger<PlayerVaultGrain>.Instance
            );

            await h.Grain.OnActivateAsync(CancellationToken.None);

            return h;
        }

        private IGrainFactory BuildGrainFactory()
        {
            IPlayerWalletGrain wallet = FakeProxy.Create<IPlayerWalletGrain>(call =>
            {
                if (call.Method.Name != nameof(IPlayerWalletGrain.CreditOnceAsync))
                {
                    return null;
                }

                WalletCredits.Add(call);

                if (FailNextCredit)
                {
                    FailNextCredit = false;
                    return Task.FromException<bool>(new InvalidOperationException("refused"));
                }

                // Models commerce_receipts: one row per (operation, step), and the credit commits
                // with it or not at all.
                if (!_receipts.Add(((CommerceOperationId)call.Args![1]!, (string)call.Args[2]!)))
                {
                    return Task.FromResult(false);
                }

                AppliedCredits.AddRange((List<WalletDebitRequest>)call.Args[0]!);

                if (ThrowAfterNextCredit)
                {
                    ThrowAfterNextCredit = false;
                    return Task.FromException<bool>(new TimeoutException("reply lost"));
                }

                return Task.FromResult(true);
            });

            IServerConfigGrain config = FakeProxy.Create<IServerConfigGrain>(call =>
                call.Method.Name == nameof(IServerConfigGrain.GetBoolAsync)
                    ? Task.FromResult(
                        Config.TryGetValue((string)call.Args![0]!, out bool value)
                            ? value
                            : (bool)call.Args[1]!
                    )
                    : null
            );

            return FakeProxy.Create<IGrainFactory>(call =>
            {
                if (!call.Method.IsGenericMethod)
                {
                    return null;
                }

                Type grainType = call.Method.GetGenericArguments()[0];

                return grainType == typeof(IPlayerWalletGrain) ? wallet
                    : grainType == typeof(IServerConfigGrain) ? config
                    : null;
            });
        }
    }

    /// <summary>A write failure that is not a duplicate key: the grain must not read it as one.</summary>
    private sealed class FailingInsertInterceptor : SaveChangesInterceptor
    {
        public bool Enabled { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        ) =>
            Enabled
            && eventData
                .Context!.ChangeTracker.Entries<PlayerVaultIncomeRewardEntity>()
                .Any(e => e.State == EntityState.Added)
                ? throw new DbUpdateException("disk full")
                : ValueTask.FromResult(result);
    }

    private sealed class InMemoryDbContextFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);
    }
}
