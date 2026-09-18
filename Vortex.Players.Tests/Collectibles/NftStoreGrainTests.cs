using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vortex.Collectibles.Grains;
using Vortex.Database.Context;
using Vortex.Database.Entities.Collectibles;
using Vortex.Database.Entities.Furniture;
using Vortex.Primitives.Collectibles;
using Vortex.Primitives.Collectibles.Grains;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Players;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Players.Tests.Collectibles;

/// <summary>
/// What the Shop tab is handed. The shop has two numbers a hotel and its client disagree about —
/// what "no limit" looks like, and whether a sold-out offer is still an offer — and both
/// disagreements are silent: the tab renders, and the buy button simply never works.
/// </summary>
public sealed class NftStoreGrainTests
{
    private const string Sofa = "club_sofa";

    /// <summary>
    ///     Zero is this hotel's "no limit" and -1 is the client's. Sending zero verbatim leaves the
    ///     tab drawing no counter (<c>mintLimit &gt; 0</c>) and, worse, a disabled buy button
    ///     (<c>mintedCount &lt; mintLimit || mintLimit === -1</c>) — an unlimited offer nobody can
    ///     buy, which is the opposite of what the row says.
    /// </summary>
    [Fact]
    public async Task AnOfferWithNoLimit_IsSentAsTheClientsUnlimited()
    {
        Harness harness = await Harness.CreateAsync(mintLimit: 0).ConfigureAwait(true);

        NftStoreOfferSnapshot offer = await harness.SingleOfferAsync().ConfigureAwait(true);

        offer.MintLimit.Should().Be(NftStoreOfferSnapshot.UnlimitedMintLimit);
        offer.MintLimit.Should().NotBe(0, "zero is a limit as far as the client is concerned");
    }

    [Fact]
    public async Task ALimitedOffer_KeepsItsRealLimit()
    {
        Harness harness = await Harness
            .CreateAsync(mintLimit: 10, soldCount: 3)
            .ConfigureAwait(true);

        NftStoreOfferSnapshot offer = await harness.SingleOfferAsync().ConfigureAwait(true);

        offer.MintLimit.Should().Be(10);
        offer.MintedCount.Should().Be(3);
    }

    /// <summary>
    ///     The client has a sold-out rendering — a full counter and a dead buy button — and it is
    ///     only reachable if the offer is sent. Dropping it from the list instead makes a limited
    ///     edition disappear from the shop the moment its last copy goes, which reads as a bug
    ///     rather than as "you missed it".
    /// </summary>
    [Fact]
    public async Task ASoldOutOffer_IsStillOnTheShelf()
    {
        Harness harness = await Harness
            .CreateAsync(mintLimit: 2, soldCount: 2)
            .ConfigureAwait(true);

        NftStoreOfferSnapshot offer = await harness.SingleOfferAsync().ConfigureAwait(true);

        offer.MintedCount.Should().Be(offer.MintLimit, "which is what the client greys out on");
    }

    /// <summary>
    ///     The other half of listing a sold-out offer: it is on the shelf to be looked at, not to be
    ///     bought. The purchase keeps its own guard, so a client that asks anyway — a stale tab, a
    ///     hand-built packet — is refused before a single emerald moves.
    /// </summary>
    [Fact]
    public async Task ASoldOutOffer_StillCannotBeBought()
    {
        Harness harness = await Harness
            .CreateAsync(mintLimit: 2, soldCount: 2)
            .ConfigureAwait(true);

        NftStorePurchaseOutcome outcome = await harness
            .Grain.PurchaseAsync(new PlayerId(101), Sofa, CancellationToken.None)
            .ConfigureAwait(true);

        outcome.Should().Be(NftStorePurchaseOutcome.SoldOut);
    }

    /// <summary>Taken off the shelf is the one thing that does hide a row.</summary>
    [Fact]
    public async Task ADisabledOffer_IsNotSent()
    {
        Harness harness = await Harness.CreateAsync(enabled: false).ConfigureAwait(true);

        ImmutableArray<NftStoreOfferSnapshot> offers = await harness
            .Grain.GetOffersAsync(CancellationToken.None)
            .ConfigureAwait(true);

        offers.Should().BeEmpty();
    }

    private sealed class Harness
    {
        private Harness(NftStoreGrain grain) => Grain = grain;

        public NftStoreGrain Grain { get; }

        public static async Task<Harness> CreateAsync(
            int mintLimit = 0,
            int soldCount = 0,
            bool enabled = true
        )
        {
            DbContextOptions<VortexDbContext> options =
                new DbContextOptionsBuilder<VortexDbContext>()
                    .UseInMemoryDatabase($"store-{Guid.NewGuid():N}")
                    .Options;

            await using (VortexDbContext seed = new(options))
            {
                seed.FurnitureDefinitions.Add(
                    new FurnitureDefinitionEntity
                    {
                        Id = 1,
                        SpriteId = 1,
                        Name = Sofa,
                        ProductType = ProductType.Floor,
                        FurniCategory = FurnitureCategory.Default,
                        Logic = "default",
                        Width = 1,
                        Length = 1,
                        StackHeight = 1,
                        CanStack = true,
                        CanWalk = false,
                        CanSit = false,
                        CanLay = false,
                        CanRecycle = false,
                        CanTrade = true,
                        CanGroup = true,
                        CanSell = true,
                    }
                );

                seed.NftStoreOffers.Add(
                    new NftStoreOfferEntity
                    {
                        Id = 1,
                        ProductCode = Sofa,
                        EmeraldPrice = 5,
                        IsLimited = mintLimit > 0,
                        MintLimit = mintLimit,
                        SoldCount = soldCount,
                        Score = 10,
                        Rarity = "common",
                        Enabled = enabled,
                    }
                );

                await seed.SaveChangesAsync().ConfigureAwait(true);
            }

            // No grain factory and no event publisher: every test here stops at or before the
            // sold-out guard, which is the first thing PurchaseAsync does and needs neither.
            NftStoreGrain grain = GrainActivationContext.CreateWithIntegerKey<NftStoreGrain>(
                0,
                new SingleOptionsFactory(options),
                null,
                null,
                NullLogger<NftStoreGrain>.Instance
            );

            await grain.ReloadAsync(CancellationToken.None).ConfigureAwait(true);

            return new Harness(grain);
        }

        public async Task<NftStoreOfferSnapshot> SingleOfferAsync()
        {
            ImmutableArray<NftStoreOfferSnapshot> offers = await Grain
                .GetOffersAsync(CancellationToken.None)
                .ConfigureAwait(true);

            return offers.Should().ContainSingle().Which;
        }

        private sealed class SingleOptionsFactory(DbContextOptions<VortexDbContext> options)
            : IDbContextFactory<VortexDbContext>
        {
            public VortexDbContext CreateDbContext() => new(options);
        }
    }
}
