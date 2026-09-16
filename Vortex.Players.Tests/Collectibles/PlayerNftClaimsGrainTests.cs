using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans;
using Vortex.Collectibles.Grains;
using Vortex.Database.Context;
using Vortex.Database.Entities.Collectibles;
using Vortex.Database.Entities.Furniture;
using Vortex.Players.Grains;
using Vortex.Primitives.Chain;
using Vortex.Primitives.Collectibles;
using Vortex.Primitives.Events;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Inventory.Grains;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Players.Tests.Collectibles;

/// <summary>
///     The Relics tab. The client has one button and it claims everything at once, so the rule that
///     matters is that a second press hands nothing over again.
/// </summary>
public sealed class PlayerNftClaimsGrainTests
{
    private const int PlayerId = 42;
    private const string Lamp = "02_dragonlamp_skream";
    private const int RelicAssetId = 91;
    private const string Wallet = "0x000000000000000000000000000000000000beef";

    [Fact]
    public async Task AnOutstandingClaim_IsListedWithTheSpriteIdAndTheWalletEchoedBack()
    {
        Harness harness = await Harness.CreateAsync().ConfigureAwait(true);

        ImmutableArray<NftClaimSnapshot> claims = await harness
            .Grain.GetClaimsAsync("0xwallet", CancellationToken.None)
            .ConfigureAwait(true);

        NftClaimSnapshot claim = claims.Should().ContainSingle().Which;

        claim.ProductCode.Should().Be(Lamp);
        claim.Wallet.Should().Be("0xwallet", "the client shows the wallet under the reward");

        // Same trap as everywhere else in this domain: the client reads the item type with parseInt.
        claim.ClaimItem.Product.ItemTypeId.Should().Be("77");
        claim.ClaimItem.Product.ItemTypeId.Should().NotBe(Lamp);
        claim.ClaimItem.Product.ProductTypeId.Should().Be(CollectibleProductIdentity.Floor);
    }

    [Fact]
    public async Task ClaimingTwice_HandsOverNothingTheSecondTime()
    {
        Harness harness = await Harness.CreateAsync(claimLimit: 2).ConfigureAwait(true);

        (await harness.Grain.ClaimAllAsync(CancellationToken.None).ConfigureAwait(true))
            .Should()
            .Be(2, "both copies are owed");

        (await harness.Grain.ClaimAllAsync(CancellationToken.None).ConfigureAwait(true))
            .Should()
            .Be(0, "the entitlement is spent");

        ImmutableArray<NftClaimSnapshot> left = await harness
            .Grain.GetClaimsAsync("0xwallet", CancellationToken.None)
            .ConfigureAwait(true);

        left.Should().BeEmpty("a spent claim is history, not a reward");
    }

    [Fact]
    public async Task AnExpiredClaim_IsNeitherListedNorClaimable()
    {
        Harness harness = await Harness
            .CreateAsync(validTo: DateTime.UtcNow.AddDays(-1))
            .ConfigureAwait(true);

        (
            await harness
                .Grain.GetClaimsAsync("0xwallet", CancellationToken.None)
                .ConfigureAwait(true)
        )
            .Should()
            .BeEmpty();
        (await harness.Grain.ClaimAllAsync(CancellationToken.None).ConfigureAwait(true))
            .Should()
            .Be(0);
    }

    /// <summary>
    ///     A claim naming furniture that does not exist must not consume itself: it would take the
    ///     entitlement away and hand over nothing, which is unrecoverable for the player.
    /// </summary>
    [Fact]
    public async Task AClaimNamingUnknownFurniture_IsLeftOutstanding()
    {
        Harness harness = await Harness
            .CreateAsync(productCode: "does_not_exist")
            .ConfigureAwait(true);

        (await harness.Grain.ClaimAllAsync(CancellationToken.None).ConfigureAwait(true))
            .Should()
            .Be(0);

        (
            await harness
                .Grain.GetClaimsAsync("0xwallet", CancellationToken.None)
                .ConfigureAwait(true)
        )
            .Should()
            .ContainSingle("it stays for an admin to fix");
    }

    /// <summary>
    ///     The other door. A Relic that left through the Transfer tab comes home when the hotel's
    ///     treasury holds its token on the chain — and claiming it destroys the token, because one
    ///     Relic owned in two places is exactly what the wallet column exists to prevent.
    /// </summary>
    [Fact]
    public async Task AReturnedRelic_IsListedForTheWalletItLeftFromAndClaimingItBurnsTheToken()
    {
        FakeChain chain = new();
        Harness harness = await Harness
            .CreateAsync(chain: chain, sentOutTo: Wallet)
            .ConfigureAwait(true);

        chain.Give(FakeChain.Treasury, RelicAssetId);

        ImmutableArray<NftClaimSnapshot> claims = await harness
            .Grain.GetClaimsAsync(Wallet, CancellationToken.None)
            .ConfigureAwait(true);

        claims
            .Select(claim => claim.ClaimId)
            .Should()
            .Contain($"relic:{RelicAssetId}", "the treasury is holding it");

        (
            await harness
                .Grain.GetClaimsAsync("0xsomewhereelse", CancellationToken.None)
                .ConfigureAwait(true)
        )
            .Select(claim => claim.ClaimId)
            .Should()
            .NotContain($"relic:{RelicAssetId}", "it is listed under the wallet it left from");

        await harness.Grain.ClaimAllAsync(CancellationToken.None).ConfigureAwait(true);

        chain.Burned.Should().ContainSingle().Which.Should().Be(RelicAssetId);

        await using VortexDbContext db = harness.Db.CreateDbContext();

        db.NftAssets.Single(asset => asset.Id == RelicAssetId)
            .WalletAddress.Should()
            .BeNull("it is back in the hotel");
    }

    /// <summary>
    ///     The chain decides, not the database. A row saying a Relic left is not evidence it came
    ///     back, and offering it as a claim would hand the player a second copy of something they
    ///     still own in their own wallet.
    /// </summary>
    [Fact]
    public async Task ARelicStillOutOnTheChain_IsNotClaimable()
    {
        FakeChain chain = new();
        Harness harness = await Harness
            .CreateAsync(chain: chain, sentOutTo: Wallet)
            .ConfigureAwait(true);

        chain.Give(Wallet, RelicAssetId);

        (await harness.Grain.GetClaimsAsync(Wallet, CancellationToken.None).ConfigureAwait(true))
            .Select(claim => claim.ClaimId)
            .Should()
            .NotContain($"relic:{RelicAssetId}");

        await harness.Grain.ClaimAllAsync(CancellationToken.None).ConfigureAwait(true);

        chain.Burned.Should().BeEmpty("the hotel does not hold it");
    }

    private sealed class Harness
    {
        private Harness(PlayerNftClaimsGrain grain, IDbContextFactory<VortexDbContext> db)
        {
            Grain = grain;
            Db = db;
        }

        public PlayerNftClaimsGrain Grain { get; }

        public IDbContextFactory<VortexDbContext> Db { get; }

        public static async Task<Harness> CreateAsync(
            string productCode = Lamp,
            int claimLimit = 1,
            DateTime? validTo = null,
            IChainService? chain = null,
            string? sentOutTo = null
        )
        {
            DbContextOptions<VortexDbContext> options =
                new DbContextOptionsBuilder<VortexDbContext>()
                    .UseInMemoryDatabase($"claims-{Guid.NewGuid():N}")
                    .Options;

            await using (VortexDbContext seed = new(options))
            {
                seed.FurnitureDefinitions.Add(
                    new FurnitureDefinitionEntity
                    {
                        Id = 77,
                        SpriteId = 77,
                        Name = Lamp,
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

                seed.NftClaims.Add(
                    new NftClaimEntity
                    {
                        Id = 1,
                        PlayerEntityId = PlayerId,
                        ClaimCode = "c1",
                        ProductCode = productCode,
                        ClaimLimit = claimLimit,
                        ValidTo = validTo,
                    }
                );

                if (sentOutTo is not null)
                {
                    seed.NftAssets.Add(
                        new NftAssetEntity
                        {
                            Id = RelicAssetId,
                            PlayerEntityId = PlayerId,
                            ProductCode = Lamp,
                            WalletAddress = sentOutTo,
                            SerialNumber = 1,
                        }
                    );
                }

                await seed.SaveChangesAsync().ConfigureAwait(true);
            }

            // Only the inventory is reached, and only to be handed a definition id.
            IInventoryGrain inventory = FakeProxy.Create<IInventoryGrain>(_ => Task.CompletedTask);

            IGrainFactory grainFactory = FakeProxy.Create<IGrainFactory>(call =>
                call.Method.IsGenericMethod
                && call.Method.GetGenericArguments()[0] == typeof(IInventoryGrain)
                    ? inventory
                    : null
            );

            SingleOptionsFactory db = new(options);

            PlayerNftClaimsGrain grain =
                GrainActivationContext.CreateWithIntegerKey<PlayerNftClaimsGrain>(
                    PlayerId,
                    db,
                    grainFactory,
                    FakeProxy.Create<IEventPublisher>(_ => Task.CompletedTask),
                    NullLogger<PlayerNftClaimsGrain>.Instance,
                    // Off unless a test says otherwise: a hotel with no chain is the default, and
                    // none of the claims above should ever reach one.
                    chain ?? new FakeChain { Enabled = false, TreasuryAddress = null }
                );

            return new Harness(grain, db);
        }

        private sealed class SingleOptionsFactory(DbContextOptions<VortexDbContext> options)
            : IDbContextFactory<VortexDbContext>
        {
            public VortexDbContext CreateDbContext() => new(options);
        }
    }
}
