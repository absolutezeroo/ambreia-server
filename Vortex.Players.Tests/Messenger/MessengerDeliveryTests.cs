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
using Vortex.Database.Entities.Messenger;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Events;
using Vortex.Primitives.FriendList.Enums;
using Vortex.Primitives.FriendList.Grains;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Server.Grains;
using Vortex.Protocol.Messages.Outgoing.FriendList;
using Vortex.Social.Configuration;
using Vortex.Social.Grains;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Players.Tests.Messenger;

/// <summary>
///     A messenger message takes two routes to the same person and only one of them was ever
///     observed working: the live push at send time, and the replay of everything still marked
///     <c>Delivered = false</c> when the receiver's grain hears <c>NotifyOnlineAsync</c>. A player
///     reported receiving messages "only when I reconnect", which is precisely what is left when
///     the live push does nothing and the replay carries the whole conversation.
///
///     Both halves are fire-and-forget (<c>LogAndForget</c> around the inter-grain call, and again
///     around the composer send inside it), so a failure on either is swallowed and shows up only
///     as a message that arrives late. These tests pin the live route and the flush that stops the
///     replay from repeating it forever.
/// </summary>
public sealed class MessengerDeliveryTests : IAsyncLifetime
{
    private const int SenderId = 1;
    private const int ReceiverId = 2;
    private const string Body = "salut";

    private DbContextOptions<VortexDbContext> _options = null!;

    /// <summary>
    ///     The in-memory provider, not SQLite: <c>EnsureCreated</c> against SQLite keeps the audit
    ///     columns NOT NULL but drops the defaults the real schema gives them, and the grain does
    ///     not fill them itself — so every insert the grain makes would fail for a reason that has
    ///     nothing to do with delivery. The one thing it costs is <c>ExecuteUpdateAsync</c>, which
    ///     the in-memory provider does not implement; that is the delivered-flush, covered nowhere
    ///     here and noted in the class remarks.
    /// </summary>
    public Task InitializeAsync()
    {
        _options = new DbContextOptionsBuilder<VortexDbContext>()
            .UseInMemoryDatabase($"messenger-{Guid.NewGuid():N}")
            .Options;

        return Harness.SeedTwoFriendsAsync(_options);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AMessageToAFriend_IsPushedToThatFriendAtSendTime()
    {
        Harness harness = await Harness.CreateAsync(_options).ConfigureAwait(true);

        await harness.SendAsync(Body).ConfigureAwait(true);

        harness
            .Pushed.Should()
            .ContainSingle(
                "the receiver is online, so the message must not wait for a reconnect to be replayed"
            );

        NewConsoleMessageMessageComposer sent = harness.Pushed.Single();

        sent.SenderId.Should().Be(SenderId);
        sent.Message.Should().Be(Body);

        // The chat is keyed by the *other* participant, which from the receiver's side is the
        // sender. Getting this wrong files the message under a conversation that does not exist.
        sent.ChatId.Should().Be(SenderId);

        // Zero, and only zero: a non-zero confirmation id sends the client into
        // onConfirmOwnChatMessage(), which drops any message it has no pending entry for.
        sent.ConfirmationId.Should().Be(0);
    }

    [Fact]
    public async Task AMessageToAnOfflineFriend_IsLeftPendingForTheLoginReplay()
    {
        // Nothing is pushed and the row stays pending, which is the state the login replay picks
        // up. The replay itself is deliberately not covered here: it reads its page size through
        // Grain.GrainFactory -- Orleans' own, not the injected factory -- and a grain built
        // outside a silo has no service provider that answers it, so the query degrades to
        // Take(0) and the test would be measuring the harness rather than the code.
        Harness harness = await Harness
            .CreateAsync(_options, receiverOnline: false)
            .ConfigureAwait(true);

        await harness.SendAsync(Body).ConfigureAwait(true);

        harness.Pushed.Should().BeEmpty();

        MessengerMessageEntity row = await harness.ReadOnlyMessageAsync().ConfigureAwait(true);

        row.Delivered.Should().BeFalse("the login replay is what has to find it later");
        row.Message.Should().Be(Body);
    }

    [Fact]
    public async Task AMessageToSomeoneISelfIgnore_IsRefusedRatherThanQuietlyBinned()
    {
        // The receiver drops an ignored sender's message on arrival, so a message sent to someone
        // on my OWN ignore list was written to the database, pushed over the wire and thrown away
        // at the far end -- with nothing said to either side. The refusal is what the client turns
        // into its msg_notification bubble.
        Harness harness = await Harness.CreateAsync(_options).ConfigureAwait(true);

        await harness.IgnoreReceiverAsync().ConfigureAwait(true);

        InstantMessageErrorCodeType? error = await harness.TrySendAsync(Body).ConfigureAwait(true);

        error.Should().Be(InstantMessageErrorCodeType.OfflineFailed);
        harness.Pushed.Should().BeEmpty();
        (await harness.StoredMessageCountAsync().ConfigureAwait(true))
            .Should()
            .Be(0, "a refused message must leave nothing for the login replay to deliver");
    }

    private sealed class Harness
    {
        private readonly DbContextOptions<VortexDbContext> _options;

        private Harness(
            MessengerGrain sender,
            MessengerGrain receiver,
            DbContextOptions<VortexDbContext> options,
            List<NewConsoleMessageMessageComposer> pushed
        )
        {
            _sender = sender;
            _receiver = receiver;
            _options = options;
            Pushed = pushed;
        }

        private readonly MessengerGrain _sender;
        private readonly MessengerGrain _receiver;

        /// <summary>Every console message that reached the receiver's presence, in order.</summary>
        public List<NewConsoleMessageMessageComposer> Pushed { get; }

        public async Task SendAsync(string message)
        {
            InstantMessageErrorCodeType? error = await TrySendAsync(message).ConfigureAwait(true);

            error.Should().BeNull("the two are friends and neither has blocked the other");
        }

        /// <summary>The same send, with the refusal handed back instead of asserted away.</summary>
        public Task<InstantMessageErrorCodeType?> TrySendAsync(string message) =>
            _sender.SendMessageAsync(
                PlayerId.Parse(ReceiverId),
                message,
                chatId: ReceiverId,
                confirmationId: 0,
                habbiconId: 0,
                CancellationToken.None
            );

        public Task IgnoreReceiverAsync() =>
            _sender.IgnoreUserAsync(PlayerId.Parse(ReceiverId), CancellationToken.None);

        public async Task<int> StoredMessageCountAsync()
        {
            await using VortexDbContext dbCtx = new(_options);

            return await dbCtx.MessengerMessages.AsNoTracking().CountAsync().ConfigureAwait(true);
        }

        public Task ReceiverGoesOnlineAsync() =>
            _receiver.NotifyOnlineAsync(CancellationToken.None);

        public async Task<MessengerMessageEntity> ReadOnlyMessageAsync()
        {
            await using VortexDbContext dbCtx = new(_options);

            return await dbCtx.MessengerMessages.AsNoTracking().SingleAsync().ConfigureAwait(true);
        }

        public static async Task<Harness> CreateAsync(
            DbContextOptions<VortexDbContext> options,
            bool receiverOnline = true
        )
        {
            List<NewConsoleMessageMessageComposer> pushed = new();

            IPlayerPresenceGrain receiverPresence = FakeProxy.Create<IPlayerPresenceGrain>(call =>
            {
                switch (call.Method.Name)
                {
                    case nameof(IPlayerPresenceGrain.IsOnlineAsync):
                        return Task.FromResult(receiverOnline);

                    case nameof(IPlayerPresenceGrain.SendComposerAsync):
                        // Only a session with a socket reaches the client: the real grain queues
                        // unconditionally but drains only behind `_sessionObserver is not null`,
                        // so a receiver with no socket swallows the composer exactly like this.
                        if (receiverOnline)
                        {
                            foreach (
                                NewConsoleMessageMessageComposer composer in Flatten(call.Args)
                                    .OfType<NewConsoleMessageMessageComposer>()
                            )
                            {
                                pushed.Add(composer);
                            }
                        }

                        return Task.CompletedTask;

                    default:
                        return null;
                }
            });

            IPlayerPresenceGrain senderPresence = FakeProxy.Create<IPlayerPresenceGrain>(call =>
                call.Method.Name == nameof(IPlayerPresenceGrain.IsOnlineAsync)
                    ? Task.FromResult(true)
                    : (object?)Task.CompletedTask
            );

            MessengerGrain? sender = null;
            MessengerGrain? receiver = null;

            IGrainFactory grainFactory = FakeProxy.Create<IGrainFactory>(call =>
            {
                if (call.Method.Name != nameof(IGrainFactory.GetGrain))
                {
                    return null;
                }

                Type wanted = call.Method.GetGenericArguments()[0];
                long key = call.Args?[0] is long l ? l : 0;

                if (wanted == typeof(IPlayerPresenceGrain))
                {
                    return key == ReceiverId ? receiverPresence : senderPresence;
                }

                // The offline replay asks this for its page size before reading anything, and a
                // stub that answers the default 0 turns the query into Take(0) — an empty replay
                // that looks exactly like a delivery bug.
                if (wanted == typeof(IServerConfigGrain))
                {
                    return FakeProxy.Create<IServerConfigGrain>(cfg =>
                        cfg.Method.Name == nameof(IServerConfigGrain.GetIntAsync)
                            ? Task.FromResult(MessengerConfig.MessageHistoryLimitDefault)
                            : null
                    );
                }

                // Both real grains, so the inter-grain hop under test runs for real rather than
                // against a stub that would report success either way. The sender's own grain is
                // needed too: going online fans presence out to every friend.
                if (wanted == typeof(IMessengerGrain))
                {
                    return key == ReceiverId ? receiver : sender;
                }

                return null;
            });

            List<PlayerId> onlinePlayerIds = receiverOnline
                ? [PlayerId.Parse(SenderId), PlayerId.Parse(ReceiverId)]
                : [PlayerId.Parse(SenderId)];

            sender = Build(SenderId, options, grainFactory, onlinePlayerIds);
            receiver = Build(ReceiverId, options, grainFactory, onlinePlayerIds);

            await sender.OnActivateAsync(CancellationToken.None).ConfigureAwait(true);
            await receiver.OnActivateAsync(CancellationToken.None).ConfigureAwait(true);

            return new Harness(sender, receiver, options, pushed);
        }

        private static MessengerGrain Build(
            int playerId,
            DbContextOptions<VortexDbContext> options,
            IGrainFactory grainFactory,
            IReadOnlyCollection<PlayerId> onlinePlayerIds
        ) =>
            GrainActivationContext.CreateWithIntegerKey<MessengerGrain>(
                playerId,
                // The offline replay reaches for Grain.GrainFactory -- Orleans' own, resolved out
                // of the activation services -- rather than the injected one every other method
                // uses, so the same fake has to answer on both routes or that path runs blind.
                call =>
                    call.Method.Name == "GetService"
                    && call.Args?[0] as Type == typeof(IGrainFactory)
                        ? grainFactory
                        : null,
                [
                    new PooledDbContextFactory(options),
                    grainFactory,
                    FakeProxy.Create<IEventPublisher>(_ => Task.CompletedTask),
                    NullLogger<MessengerGrain>.Instance,
                    Options.Create(new MessengerConfig()),
                    // FriendsWhoCanSeeThis() spreads this straight into a set, so a stub that
                    // answers null there takes the presence fan-out down with a NullReference.
                    FakeProxy.Create<ISessionGateway>(call =>
                        call.Method.Name == nameof(ISessionGateway.GetOnlinePlayerIds)
                            ? onlinePlayerIds
                            : null
                    ),
                ]
            );

        /// <summary>SendComposerAsync has a single-composer and a params overload; both land here.</summary>
        private static IEnumerable<object?> Flatten(object?[]? args)
        {
            foreach (object? arg in args ?? Array.Empty<object?>())
            {
                if (arg is IComposer[] many)
                {
                    foreach (IComposer one in many)
                    {
                        yield return one;
                    }

                    continue;
                }

                yield return arg;
            }
        }

        internal static async Task SeedTwoFriendsAsync(DbContextOptions<VortexDbContext> options)
        {
            await using VortexDbContext seed = new(options);

            PlayerEntity sender = NewPlayer(SenderId, "Sender");
            PlayerEntity receiver = NewPlayer(ReceiverId, "Receiver");

            await seed.Players.AddRangeAsync(sender, receiver).ConfigureAwait(true);
            await seed
                .MessengerFriends.AddRangeAsync(
                    NewFriendship(sender, receiver),
                    NewFriendship(receiver, sender)
                )
                .ConfigureAwait(true);

            await seed.SaveChangesAsync().ConfigureAwait(true);
        }

        private static PlayerEntity NewPlayer(int id, string name) =>
            new()
            {
                Id = id,
                Name = name,
                Figure = "hd-180-1.ch-210-66.lg-270-82.sh-290-81",
                Motto = string.Empty,
                Gender = AvatarGenderType.Male,
                PlayerStatus = PlayerStatusType.Offline,
                PlayerPerks = PlayerPerkFlags.None,

                // The live schema fills this column itself; EnsureCreated against SQLite keeps the
                // NOT NULL and drops the default, so a seeded row has to carry its own.
                CreatedAt = DateTime.UtcNow,
            };

        private static MessengerFriendEntity NewFriendship(PlayerEntity of, PlayerEntity with) =>
            new()
            {
                PlayerEntityId = of.Id,
                FriendPlayerEntityId = with.Id,
                RelationType = MessengerFriendRelationType.Zero,
                PlayerEntity = of,
                FriendPlayerEntity = with,
                CreatedAt = DateTime.UtcNow,
            };
    }

    private sealed class PooledDbContextFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);
    }
}
