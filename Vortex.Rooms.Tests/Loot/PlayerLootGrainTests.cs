using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans;
using Vortex.Database.Context;
using Vortex.Primitives.Events;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Furniture.Snapshots;
using Vortex.Primitives.Loot;
using Vortex.Primitives.Loot.Snapshots;
using Vortex.Primitives.Players.Grains;
using Vortex.Progression.Grains;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Loot;

/// <summary>
/// The grant grain raises the payout audit itself, so that a new reward furniture cannot ship with
/// its draws off the trail. That only holds if the event fires exactly when something was really
/// handed over — an event on a failed grant would invent payouts in the pool statistics, and a
/// missing one would hide a real prize from a dispute.
/// </summary>
public sealed class PlayerLootGrainTests
{
    private const int PLAYER_ID = 77;
    private const string SOURCE = "test-trigger";
    private const string POOL_CODE = "test-pool";

    [Fact]
    public async Task GrantingAnEffect_PublishesThePayoutWithItsPoolAndSource()
    {
        Harness harness = new Harness();

        LootAward? award = await harness
            .Grain.GrantAsync(
                Entry(ProductType.Effect, extraParam: "42:600:3"),
                SOURCE,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        award!.ClassId.Should().Be(42);
        award.ContentType.Should().Be(ProductType.Effect.ToLegacyString());

        harness.AddedEffects.Should().Equal((42, 3, 600));

        LootAwardedEvent published = harness.Published.Should().ContainSingle().Subject;
        published.PlayerId.Should().Be(PLAYER_ID);
        published.PoolCode.Should().Be(POOL_CODE);
        published.EntryId.Should().Be(9);
        published.Variant.Should().Be("blue");
        published.Source.Should().Be(SOURCE);
        published.ClassId.Should().Be(42);
    }

    [Fact]
    public async Task AProductTypeThatCannotBeGranted_AwardsNothingAndPublishesNothing()
    {
        Harness harness = new Harness();

        LootAward? award = await harness
            .Grain.GrantAsync(Entry(ProductType.Pet), SOURCE, CancellationToken.None)
            .ConfigureAwait(true);

        award.Should().BeNull();
        harness.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task AMissingFurnitureDefinition_AwardsNothingAndPublishesNothing()
    {
        // The provider answers null for every id, which is what a prize pointing at a definition the
        // hotel does not ship looks like.
        Harness harness = new Harness();

        LootAward? award = await harness
            .Grain.GrantAsync(Entry(ProductType.Floor), SOURCE, CancellationToken.None)
            .ConfigureAwait(true);

        award.Should().BeNull();
        harness.Published.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-number")]
    [InlineData("0")]
    [InlineData("-3")]
    public async Task MalformedEffectParameters_AwardNothingAndPublishNothing(string extraParam)
    {
        Harness harness = new Harness();

        LootAward? award = await harness
            .Grain.GrantAsync(Entry(ProductType.Effect, extraParam), SOURCE, CancellationToken.None)
            .ConfigureAwait(true);

        award.Should().BeNull();
        harness.AddedEffects.Should().BeEmpty();
        harness.Published.Should().BeEmpty();
    }

    private static LootEntrySnapshot Entry(ProductType productType, string extraParam = "") =>
        new()
        {
            Id = 9,
            PoolCode = POOL_CODE,
            Variant = "blue",
            ProductType = productType,
            FurnitureDefinitionId = 1234,
            ExtraParam = extraParam,
            Weight = 1,
        };

    private sealed class Harness
    {
        public Harness()
        {
            Grain = GrainActivationContext.CreateWithIntegerKey<PlayerLootGrain>(
                PLAYER_ID,
                new TestDbContextFactory(
                    new DbContextOptionsBuilder<VortexDbContext>()
                        .UseInMemoryDatabase($"prize-grant-{Guid.NewGuid()}")
                        .Options
                ),
                BuildGrainFactory(),
                BuildDefinitionProvider(),
                BuildEventPublisher(),
                NullLogger<PlayerLootGrain>.Instance
            );
        }

        public PlayerLootGrain Grain { get; }

        public List<LootAwardedEvent> Published { get; } = [];

        public List<(int EffectId, int SubType, int Duration)> AddedEffects { get; } = [];

        private IGrainFactory BuildGrainFactory() =>
            FakeProxy.Create<IGrainFactory>(call =>
                call.Method.IsGenericMethod
                && call.Method.GetGenericArguments()[0] == typeof(IPlayerEffectGrain)
                    ? BuildEffectGrain()
                    : null
            );

        private IPlayerEffectGrain BuildEffectGrain() =>
            FakeProxy.Create<IPlayerEffectGrain>(call =>
            {
                if (call.Method.Name != nameof(IPlayerEffectGrain.AddEffectAsync))
                {
                    return null;
                }

                AddedEffects.Add(((int)call.Args![0]!, (int)call.Args[1]!, (int)call.Args[2]!));

                return Task.FromResult(true);
            });

        private static IFurnitureDefinitionProvider BuildDefinitionProvider() =>
            FakeProxy.Create<IFurnitureDefinitionProvider>(call =>
                call.Method.Name == nameof(IFurnitureDefinitionProvider.TryGetDefinition)
                    ? (FurnitureDefinitionSnapshot?)null
                    : null
            );

        private sealed class TestDbContextFactory(DbContextOptions<VortexDbContext> options)
            : IDbContextFactory<VortexDbContext>
        {
            public VortexDbContext CreateDbContext() => new VortexDbContext(options);
        }

        private IEventPublisher BuildEventPublisher() =>
            FakeProxy.Create<IEventPublisher>(call =>
            {
                if (call.Args?[0] is LootAwardedEvent awarded)
                {
                    Published.Add(awarded);
                }

                return Task.CompletedTask;
            });
    }
}
