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
using Vortex.Database.Entities.Players;
using Vortex.Players.Configuration;
using Vortex.Players.Grains;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Events;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Players.Providers;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Progression.Achievements;
using Vortex.Protocol.Messages.Outgoing.Nft;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Players;

/// <summary>
/// A chat bubble the player owns has to reach the client as the CLIENT's style id, and it has to
/// reach it without a reconnect.
///
/// <para>
/// <c>RoomChatInputView</c> keeps any style in 1000..9999 out of its selector unless
/// <c>hasNftChatStyle(id)</c> says so, and that list comes from <c>UserNftChatStyles</c> and nothing
/// else. Two ways to get this wrong and see nothing either time: send the ownership row's foreign
/// key instead of the library id, or write the row and never tell the connected client.
/// </para>
/// </summary>
public sealed class ChatStyleUnlockTests
{
    private const int PlayerId = 733;

    /// <summary>A style id in the band the client asks the server about.</summary>
    private const int UnlockableStyleId = 1004;

    [Fact]
    public async Task AGrantedStyle_ComesBackAsTheClientsOwnId()
    {
        Harness harness = await Harness.CreateAsync().ConfigureAwait(true);

        await harness
            .Grain.GrantChatStyleAsync(UnlockableStyleId, CancellationToken.None)
            .ConfigureAwait(true);

        List<int> owned = await harness
            .Grain.GetOwnedChatStyleIdsAsync(CancellationToken.None)
            .ConfigureAwait(true);

        // Not the player_chat_styles primary key, which for the first row ever written is 1 — a
        // number the client would look up, fail to match against its library, and silently drop.
        owned.Should().Equal(UnlockableStyleId);
    }

    [Fact]
    public async Task AGrantPushesTheWholeListToAConnectedPlayer()
    {
        Harness harness = await Harness.CreateAsync().ConfigureAwait(true);

        await harness
            .Grain.GrantChatStyleAsync(UnlockableStyleId, CancellationToken.None)
            .ConfigureAwait(true);

        // The client replaces its vector outright on receipt, so the refresh is the full list and
        // there is nothing else that would update a player who is already in a room.
        harness
            .Pushed.Should()
            .ContainSingle()
            .Which.ChatStyleIds.Should()
            .Equal(UnlockableStyleId);
    }

    [Fact]
    public async Task GrantingTwice_LeavesOneUnlockAndStillReportsSuccess()
    {
        Harness harness = await Harness.CreateAsync().ConfigureAwait(true);

        await harness
            .Grain.GrantChatStyleAsync(UnlockableStyleId, CancellationToken.None)
            .ConfigureAwait(true);

        bool second = await harness
            .Grain.GrantChatStyleAsync(UnlockableStyleId, CancellationToken.None)
            .ConfigureAwait(true);

        // A reward-track step and an operator both retry; neither should read "already held" as a
        // failure, and neither should leave a duplicate behind.
        second.Should().BeTrue();

        List<int> owned = await harness
            .Grain.GetOwnedChatStyleIdsAsync(CancellationToken.None)
            .ConfigureAwait(true);

        owned.Should().Equal(UnlockableStyleId);
    }

    [Theory]
    // Below the band: the client gates these itself (free, HC, ambassador, staff) and never asks.
    [InlineData(12)]
    [InlineData(999)]
    // At and above it the client reads UserPurchasableChatStyles instead, which nothing composes —
    // a grant here would be an unlock the player could never be told about.
    [InlineData(10000)]
    public async Task AnIdOutsideTheUnlockableBand_IsRefusedAndWritesNothing(int styleId)
    {
        Harness harness = await Harness.CreateAsync().ConfigureAwait(true);

        bool granted = await harness
            .Grain.GrantChatStyleAsync(styleId, CancellationToken.None)
            .ConfigureAwait(true);

        granted.Should().BeFalse();

        List<int> owned = await harness
            .Grain.GetOwnedChatStyleIdsAsync(CancellationToken.None)
            .ConfigureAwait(true);

        owned.Should().BeEmpty();
        harness.Pushed.Should().BeEmpty();
    }

    [Fact]
    public async Task RevokingTakesItBackAndPushesTheShorterList()
    {
        Harness harness = await Harness.CreateAsync().ConfigureAwait(true);

        await harness
            .Grain.GrantChatStyleAsync(UnlockableStyleId, CancellationToken.None)
            .ConfigureAwait(true);

        bool revoked = await harness
            .Grain.RevokeChatStyleAsync(UnlockableStyleId, CancellationToken.None)
            .ConfigureAwait(true);

        revoked.Should().BeTrue();

        List<int> owned = await harness
            .Grain.GetOwnedChatStyleIdsAsync(CancellationToken.None)
            .ConfigureAwait(true);

        owned.Should().BeEmpty();
        harness.Pushed.Should().HaveCount(2);
        harness.Pushed[^1].ChatStyleIds.Should().BeEmpty();
    }

    [Fact]
    public async Task RevokingSomethingTheyNeverHad_ReportsFailureAndPushesNothing()
    {
        Harness harness = await Harness.CreateAsync().ConfigureAwait(true);

        bool revoked = await harness
            .Grain.RevokeChatStyleAsync(UnlockableStyleId, CancellationToken.None)
            .ConfigureAwait(true);

        revoked.Should().BeFalse();
        harness.Pushed.Should().BeEmpty();
    }

    [Fact]
    public async Task AnotherPlayersUnlock_IsNotThisPlayers()
    {
        Harness harness = await Harness.CreateAsync().ConfigureAwait(true);

        await harness
            .Grain.GrantChatStyleAsync(UnlockableStyleId, CancellationToken.None)
            .ConfigureAwait(true);

        await using (VortexDbContext db = harness.NewContext())
        {
            PlayerChatStyleEntity style = await db
                .PlayerChatStyles.FirstAsync(s => s.ClientStyleId == UnlockableStyleId)
                .ConfigureAwait(true);

            PlayerEntity stranger = await db
                .Players.FirstAsync(p => p.Id == OtherPlayerId)
                .ConfigureAwait(true);

            db.PlayerOwnedChatStyles.Add(
                new PlayerChatStyleOwnedEntity
                {
                    PlayerEntityId = OtherPlayerId,
                    ChatStyleId = style.Id,
                    PlayerEntity = stranger,
                    ChatStyle = style,
                }
            );

            await db.SaveChangesAsync().ConfigureAwait(true);
        }

        List<int> owned = await harness
            .Grain.GetOwnedChatStyleIdsAsync(CancellationToken.None)
            .ConfigureAwait(true);

        // The catalogue row is shared; the ownership row is not. Reading the catalogue instead would
        // hand everyone every style the hotel has ever granted to anyone.
        owned.Should().Equal(UnlockableStyleId);
    }

    private const int OtherPlayerId = 734;

    private sealed class TestDbContextFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);
    }

    private sealed class Harness
    {
        private readonly DbContextOptions<VortexDbContext> _options;

        private Harness(DbContextOptions<VortexDbContext> options) => _options = options;

        public PlayerGrain Grain { get; private set; } = null!;

        /// <summary>Every <c>UserNftChatStyles</c> the grain handed to the presence grain, in order.</summary>
        public List<UserNftChatStylesMessageComposer> Pushed { get; } = [];

        public VortexDbContext NewContext() => new(_options);

        public static async Task<Harness> CreateAsync()
        {
            DbContextOptions<VortexDbContext> options =
                new DbContextOptionsBuilder<VortexDbContext>()
                    .UseInMemoryDatabase($"chat-styles-{Guid.NewGuid():N}")
                    .Options;

            await using (VortexDbContext seed = new(options))
            {
                seed.Players.Add(NewPlayer(PlayerId, "tester"));
                seed.Players.Add(NewPlayer(OtherPlayerId, "stranger"));

                await seed.SaveChangesAsync().ConfigureAwait(true);
            }

            Harness harness = new(options);

            IGrainFactory grainFactory = FakeProxy.Create<IGrainFactory>(call =>
                call.Method.Name == nameof(IGrainFactory.GetGrain)
                    ? FakeProxy.Create<IPlayerPresenceGrain>(presenceCall =>
                        presenceCall.Method.Name == nameof(IPlayerPresenceGrain.SendComposerAsync)
                            ? harness.Record(presenceCall)
                            : null
                    )
                    : null
            );

            harness.Grain = GrainActivationContext.CreateWithIntegerKey<PlayerGrain>(
                PlayerId,
                new TestDbContextFactory(options),
                grainFactory,
                FakeProxy.Create<IEventPublisher>(_ => null),
                NullLogger<PlayerGrain>.Instance,
                Options.Create(new ClubConfig()),
                FakeProxy.Create<IAccountLevelProvider>(_ => AccountLevelLadder.FloorLevel),
                FakeProxy.Create<ICommerceJournal>(_ => Task.CompletedTask)
            );

            return harness;
        }

        private object Record(ProxyCall call)
        {
            foreach (
                UserNftChatStylesMessageComposer composer in (call.Args ?? [])
                    .OfType<IComposer>()
                    .OfType<UserNftChatStylesMessageComposer>()
            )
            {
                Pushed.Add(composer);
            }

            return Task.CompletedTask;
        }

        private static PlayerEntity NewPlayer(int id, string name) =>
            new()
            {
                Id = id,
                Name = name,
                Figure = "hd-1-1",
                Gender = AvatarGenderType.Male,
                PlayerStatus = PlayerStatusType.Offline,
                PlayerPerks = PlayerPerkFlags.None,
            };
    }
}
