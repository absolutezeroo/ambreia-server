using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans;
using Vortex.Dashboard.API.Admin.Hotel;
using Vortex.Dashboard.API.Admin.Progression;
using Vortex.Database.Context;
using Vortex.Primitives.Content;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Players.Providers;
using Vortex.Primitives.Players.Snapshots;
using Vortex.Primitives.Players.Wallet;
using Vortex.Primitives.Quests.Admin;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Players.Tests.Wallet;

/// <summary>
///     A reward in a currency the hotel has no enabled row for is paid by nobody: the wallet grant
///     no-ops and the player simply never sees it, while the admin write that promised it reported
///     success. These pin the check that now stands between the two.
/// </summary>
public sealed class CurrencyRewardRulesTests
{
    [Fact]
    public void ANegativeRewardType_NamesCredits()
    {
        CurrencyRewardRules
            .KindFor(-1)
            .Should()
            .Be(new CurrencyKind { CurrencyType = CurrencyType.Credits });
    }

    [Fact]
    public void ANonNegativeRewardType_NamesThatActivityPointCurrency()
    {
        CurrencyRewardRules
            .KindFor(5)
            .Should()
            .Be(
                new CurrencyKind
                {
                    CurrencyType = CurrencyType.ActivityPoints,
                    ActivityPointType = 5,
                }
            );
    }

    [Fact]
    public void ARewardInACurrencyTheHotelHas_Passes()
    {
        CurrencyRewardRules
            .Validate(StubCurrencies.WithDuckets(), rewardType: 0, amount: 10)
            .Should()
            .BeNull();
    }

    [Fact]
    public void ARewardInACurrencyWithNoRow_IsRefused()
    {
        CurrencyRewardRules
            .Validate(StubCurrencies.WithDuckets(), rewardType: 5, amount: 10)
            .Should()
            .Be("reward_currency_unknown", "diamonds have no currency_types row in this hotel");
    }

    [Fact]
    public void ARewardInADisabledCurrency_IsRefused()
    {
        CurrencyRewardRules
            .Validate(StubCurrencies.WithDisabledDuckets(), rewardType: 0, amount: 10)
            .Should()
            .Be("reward_currency_disabled", "the grant checks enabled, not merely present");
    }

    [Fact]
    public void ALevelThatPaysNothing_IsNotHeldToACurrency()
    {
        CurrencyRewardRules
            .Validate(StubCurrencies.Empty(), rewardType: 5, amount: 0)
            .Should()
            .BeNull("an amount of zero configures no reward, so there is nothing to pay it in");
    }

    [Theory]
    [InlineData("credits", -1)]
    [InlineData("CREDITS", -1)]
    [InlineData("0", 0)]
    [InlineData("5", 5)]
    public void TheStringForm_ReadsBackToTheRewardType(string rewardTypeId, int expected)
    {
        CurrencyRewardRules.TryParseNamed(rewardTypeId, out int rewardType).Should().BeTrue();
        rewardType.Should().Be(expected);
    }

    [Fact]
    public void ADailyTaskRewardThatIsAnItem_IsNotACurrencyAndIsLeftAlone()
    {
        CurrencyRewardRules.TryParseNamed("throne", out _).Should().BeFalse();

        CurrencyRewardRules
            .ValidateNamed(StubCurrencies.Empty(), "throne", amount: 1)
            .Should()
            .BeNull("item rewards are the task grain's business, not this rule's");
    }

    /// <summary>
    ///     Whose business it is: the admin service, on the way in. Nothing grants an item on a daily
    ///     task, so a board configured to promise one promises what no code path can pay — and the
    ///     player who completes it is simply owed something that will never arrive.
    /// </summary>
    [Fact]
    public async Task SavingADailyTask_RefusesARewardNothingCanHandOver()
    {
        QuestContentAdminService service = new(
            new UnusedDbContextFactory(),
            FakeProxy.Create<IGrainFactory>(_ => null),
            StubCurrencies.WithDuckets(),
            NullLogger<QuestContentAdminService>.Instance
        );

        QuestContentAdminResult result = await service
            .CreateDailyTaskAsync(
                new DailyTaskSpec(
                    TaskCode: "sit_on_it",
                    QuestTypeCode: "SIT",
                    IsBonus: false,
                    ImageVersion: "1",
                    CatalogName: "Sit on it",
                    RequiredRepeats: 1,
                    Enabled: true,
                    SortOrder: 0,
                    Rewards: [new DailyTaskRewardSpec(0, "throne", string.Empty, 1)]
                ),
                CancellationToken.None
            )
            .ConfigureAwait(true);

        result.Success.Should().BeFalse("the task would promise a throne and pay nothing");
        result.ErrorCode.Should().Contain("throne");
    }

    [Fact]
    public async Task SavingAnAchievementLevel_RefusesARewardNobodyWouldBePaid()
    {
        ContentAdminService service = new(
            new UnusedDbContextFactory(),
            FakeProxy.Create<IGrainFactory>(_ => null),
            StubCurrencies.WithDuckets(),
            NullLogger<ContentAdminService>.Instance
        );

        ContentAdminResult result = await service
            .UpsertAchievementLevelAsync(
                achievementId: 1,
                new AchievementLevelSpec(
                    Level: 1,
                    BadgeCode: "ACH_RoomEntry1",
                    ProgressRequirement: 5,
                    RewardAmount: 10,
                    RewardType: 5,
                    ScorePoints: 1
                ),
                CancellationToken.None
            )
            .ConfigureAwait(true);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("reward_currency_unknown");
    }

    /// <summary>The reward check runs before anything is read, so a rejected write never gets here.</summary>
    private sealed class UnusedDbContextFactory : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() =>
            throw new System.InvalidOperationException(
                "A refused write must not reach the database."
            );
    }

    private sealed class StubCurrencies(Dictionary<CurrencyKind, CurrencyTypeSnapshot> byKind)
        : ICurrencyTypeProvider
    {
        public static StubCurrencies Empty() => new([]);

        public static StubCurrencies WithDuckets(bool enabled = true) =>
            new(
                new Dictionary<CurrencyKind, CurrencyTypeSnapshot>
                {
                    [
                        new CurrencyKind
                        {
                            CurrencyType = CurrencyType.ActivityPoints,
                            ActivityPointType = 0,
                        }
                    ] = new()
                    {
                        Id = 1,
                        Name = "Duckets",
                        CurrencyType = CurrencyType.ActivityPoints,
                        ActivityPointType = 0,
                        Enabled = enabled,
                    },
                }
            );

        public static StubCurrencies WithDisabledDuckets() => WithDuckets(enabled: false);

        public CurrencyTypeSnapshot? GetCurrencyType(int typeId) => null;

        public CurrencyTypeSnapshot? GetCurrencyTypeByKind(CurrencyKind kind) =>
            byKind.TryGetValue(kind, out CurrencyTypeSnapshot? snapshot) ? snapshot : null;

        public Task ReloadAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
