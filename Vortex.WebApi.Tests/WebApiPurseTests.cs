using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Entities.Catalog;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Rooms.Enums;
using Vortex.WebApi.Configuration;
using Vortex.WebApi.Services;
using Xunit;

/// <summary>
/// The purse read, and the shape of wallet it has to survive.
/// </summary>
/// <remarks>
/// A wallet is keyed by <c>CurrencyKind</c> — the PAIR (CurrencyType, ActivityPointType) — so
/// <c>CurrencyType.ActivityPoints</c> is a family and not a currency: duckets, diamonds and every
/// seasonal currency sit under it with their own <c>activity_point_type</c>. A hotel carrying two of
/// them is ordinary, and this route used to answer 500 on every one of them: it keyed a dictionary
/// on <c>CurrencyType</c> alone and <c>ToDictionaryAsync</c> threw on the second row — while
/// building an entry the purse never reads.
/// </remarks>
namespace Vortex.WebApi.Tests;

public sealed class WebApiPurseTests
{
    /// <summary>
    /// Every member throws: nothing in a purse read has any business reaching Orleans. Not
    /// `sealed` — `DispatchProxy.Create` derives from it.
    /// </summary>
    private class UnusedGrainFactory : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException(
                $"the purse read does not use Orleans, but {targetMethod?.Name} was called"
            );
    }

    private sealed class TestDbContextFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);

        public Task<VortexDbContext> CreateDbContextAsync(CancellationToken ct = default) =>
            Task.FromResult(CreateDbContext());
    }

    /// <summary>
    /// A player, the three currencies the purse shows, and however many activity-point currencies
    /// the caller asks for.
    /// </summary>
    private static async Task<(WebApiPlayerService Service, int PlayerId)> BuildAsync(
        int activityPointCurrencies
    )
    {
        DbContextOptions<VortexDbContext> options = new DbContextOptionsBuilder<VortexDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        TestDbContextFactory factory = new(options);

        await using VortexDbContext db = factory.CreateDbContext();

        PlayerAccountEntity account = new() { Email = "player@example.com", PasswordHash = "x" };

        db.PlayerAccounts.Add(account);
        await db.SaveChangesAsync();

        PlayerEntity player = new()
        {
            Name = "Tester",
            Figure = "hd-180-1",
            Gender = AvatarGenderType.Male,
            PlayerStatus = PlayerStatusType.Offline,
            PlayerPerks = PlayerPerkFlags.None,
            PlayerAccountEntityId = account.Id,
        };

        db.Players.Add(player);
        await db.SaveChangesAsync();

        async Task AddAsync(CurrencyType type, int? activityPointType, int amount)
        {
            CurrencyTypeEntity currency = new()
            {
                Name = $"{type}{activityPointType}",
                CurrencyType = type,
                ActivityPointType = activityPointType,
                Enabled = true,
            };

            db.CurrencyTypes.Add(currency);
            await db.SaveChangesAsync();

            db.PlayerCurrencies.Add(
                new PlayerCurrencyEntity
                {
                    PlayerEntityId = player.Id,
                    CurrencyTypeEntityId = currency.Id,
                    Amount = amount,
                }
            );

            await db.SaveChangesAsync();
        }

        await AddAsync(CurrencyType.Credits, null, 12480);
        await AddAsync(CurrencyType.Emeralds, null, 36);
        await AddAsync(CurrencyType.Silver, null, 2145);

        // The family. One is ordinary; two is what took the route down.
        for (int index = 0; index < activityPointCurrencies; index++)
        {
            await AddAsync(CurrencyType.ActivityPoints, index, 100 + index);
        }

        // The grain factory is never reached: the purse read is pure EF. A proxy that throws on any
        // call is what makes a future one fail loudly here instead of silently taking a default —
        // a `DispatchProxy`, which is cheaper than hand-writing a dozen `IGrainFactory` members nothing calls.
        return (
            new WebApiPlayerService(
                factory,
                DispatchProxy.Create<IGrainFactory, UnusedGrainFactory>(),
                new FakeWordFilter(),
                Options.Create(new WebApiConfig()),
                NullLogger<WebApiPlayerService>.Instance
            ),
            player.Id
        );
    }

    [Fact]
    public async Task Purse_ReadsTheThreeCurrenciesItShows()
    {
        (WebApiPlayerService service, int playerId) = await BuildAsync(activityPointCurrencies: 0);

        PlayerPurse? purse = await service.GetPurseAsync(playerId, CancellationToken.None);

        purse.Should().NotBeNull();
        purse!.Credits.Should().Be(12480);
        purse.Diamonds.Should().Be(36);
        purse.Duckets.Should().Be(2145);
    }

    [Fact]
    public async Task Purse_SurvivesTwoActivityPointCurrencies()
    {
        // The regression. Two rows under CurrencyType.ActivityPoints is an ordinary hotel — duckets
        // and diamonds are both activity points on habbo — and this answered 500 on all of them.
        (WebApiPlayerService service, int playerId) = await BuildAsync(activityPointCurrencies: 2);

        PlayerPurse? purse = await service.GetPurseAsync(playerId, CancellationToken.None);

        purse.Should().NotBeNull();
        purse!.Credits.Should().Be(12480);
        purse.Diamonds.Should().Be(36);
        purse.Duckets.Should().Be(2145);
    }

    [Fact]
    public async Task Purse_SurvivesFiveOfThem()
    {
        // Seasonal currencies pile up. Nothing about the number may matter.
        (WebApiPlayerService service, int playerId) = await BuildAsync(activityPointCurrencies: 5);

        (await service.GetPurseAsync(playerId, CancellationToken.None)).Should().NotBeNull();
    }

    [Fact]
    public async Task Purse_IsNullForAnUnknownPlayer()
    {
        (WebApiPlayerService service, _) = await BuildAsync(activityPointCurrencies: 0);

        (await service.GetPurseAsync(9999, CancellationToken.None)).Should().BeNull();
    }
}
