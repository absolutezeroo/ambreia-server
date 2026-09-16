using System;
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
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Chain;
using Vortex.Primitives.Collectibles.Grains;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Events;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Players.Tests.Collectibles;

/// <summary>
///     The door out of the hotel. The destination arrives from the client, so the rule that matters
///     is that it is only ever an address the player proved on the website.
/// </summary>
public sealed class PlayerMintGrainTransferTests
{
    private const int PlayerId = 42;
    private const int RelicAssetId = 91;
    private const string Lamp = "02_dragonlamp_skream";
    private const string Mine = "0x000000000000000000000000000000000000beef";
    private const string Somebody = "0x000000000000000000000000000000000000dead";

    [Fact]
    public async Task AnAddressThePlayerNeverProved_TransfersNothing()
    {
        FakeChain chain = new();
        Harness harness = await Harness.CreateAsync(chain).ConfigureAwait(true);

        NftTransferResult result = await harness
            .Grain.TransferAssetsAsync(Somebody, CancellationToken.None)
            .ConfigureAwait(true);

        result.Outcome.Should().Be(NftTransferOutcome.WalletNotLinked);
        result.Transferred.Should().Be(0);

        chain.Holdings(Somebody).Should().BeEmpty("nothing may be minted to a stranger's address");

        await using VortexDbContext db = harness.Db.CreateDbContext();

        db.NftAssets.Single().WalletAddress.Should().BeNull("the Relic never left");
    }

    [Fact]
    public async Task ALinkedAddress_GetsTheRelicMintedToItAndTheHotelStopsCountingIt()
    {
        FakeChain chain = new();
        Harness harness = await Harness.CreateAsync(chain).ConfigureAwait(true);

        NftTransferResult result = await harness
            .Grain.TransferAssetsAsync(Mine, CancellationToken.None)
            .ConfigureAwait(true);

        result.Outcome.Should().Be(NftTransferOutcome.Transferred);
        result.Transferred.Should().Be(1);

        chain.Holdings(Mine).Should().ContainSingle().Which.Should().Be(RelicAssetId);

        await using VortexDbContext db = harness.Db.CreateDbContext();

        db.NftAssets.Single().WalletAddress.Should().Be(Mine);

        (await harness.Grain.GetAssetsAsync(CancellationToken.None).ConfigureAwait(true))
            .Should()
            .BeEmpty("a Relic out on the chain counts for nobody in the hotel");
    }

    /// <summary>
    ///     Minting an id the contract already has reverts, so a transfer that stopped halfway must
    ///     skip what the wallet is already holding rather than fail on its first token forever.
    /// </summary>
    [Fact]
    public async Task ARelicAlreadyOnTheChain_IsMarkedGoneRatherThanMintedTwice()
    {
        FakeChain chain = new();
        Harness harness = await Harness.CreateAsync(chain).ConfigureAwait(true);

        chain.Give(Mine, RelicAssetId);

        NftTransferResult result = await harness
            .Grain.TransferAssetsAsync(Mine, CancellationToken.None)
            .ConfigureAwait(true);

        result.Outcome.Should().Be(NftTransferOutcome.Transferred);

        await using VortexDbContext db = harness.Db.CreateDbContext();

        db.NftAssets.Single().WalletAddress.Should().Be(Mine);
    }

    [Fact]
    public async Task NoChain_RefusesBeforeItLooksAtAnything()
    {
        Harness harness = await Harness
            .CreateAsync(new FakeChain { Enabled = false, TreasuryAddress = null })
            .ConfigureAwait(true);

        (await harness.Grain.TransferAssetsAsync(Mine, CancellationToken.None).ConfigureAwait(true))
            .Outcome.Should()
            .Be(NftTransferOutcome.ChainUnavailable);
    }

    private sealed class Harness
    {
        private Harness(PlayerMintGrain grain, IDbContextFactory<VortexDbContext> db)
        {
            Grain = grain;
            Db = db;
        }

        public PlayerMintGrain Grain { get; }

        public IDbContextFactory<VortexDbContext> Db { get; }

        public static async Task<Harness> CreateAsync(IChainService chain)
        {
            DbContextOptions<VortexDbContext> options =
                new DbContextOptionsBuilder<VortexDbContext>()
                    .UseInMemoryDatabase($"transfer-{Guid.NewGuid():N}")
                    .Options;

            await using (VortexDbContext seed = new(options))
            {
                seed.NftAssets.Add(
                    new NftAssetEntity
                    {
                        Id = RelicAssetId,
                        PlayerEntityId = PlayerId,
                        ProductCode = Lamp,
                        SerialNumber = 1,
                    }
                );

                seed.PlayerWallets.Add(
                    new PlayerWalletEntity { PlayerEntityId = PlayerId, Address = Mine }
                );

                await seed.SaveChangesAsync().ConfigureAwait(true);
            }

            SingleOptionsFactory db = new(options);

            // Nothing below the grain is reached on these paths: the fee is zero, so no purse and no
            // journal entry. A test that needed them would be testing the commerce pipeline, which
            // has its own.
            PlayerMintGrain grain = GrainActivationContext.CreateWithIntegerKey<PlayerMintGrain>(
                PlayerId,
                db,
                FakeProxy.Create<IGrainFactory>(_ => null),
                FakeProxy.Create<IEventPublisher>(_ => Task.CompletedTask),
                NullLogger<PlayerMintGrain>.Instance,
                FakeProxy.Create<ICommerceJournal>(_ => Task.CompletedTask),
                chain
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
