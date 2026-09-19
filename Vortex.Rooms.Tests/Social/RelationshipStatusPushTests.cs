using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Entities.Messenger;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Events;
using Vortex.Primitives.FriendList.Enums;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Snapshots.FriendList;
using Vortex.Protocol.Messages.Outgoing.FriendList;
using Vortex.Social.Configuration;
using Vortex.Social.Grains;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Social;

/// <summary>
/// The heart/smile/skull a player puts beside a friend's name. Persisting it is half the job: the
/// console draws the decorator from its own copy of the entry, so without a pushed update the
/// player's own choice only appears after a relog.
/// </summary>
/// <remarks>
/// SQLite rather than the in-memory provider: the write is an <c>ExecuteUpdateAsync</c>, which the
/// in-memory provider has no store-specific implementation for and throws on. A test that cannot
/// run the real statement proves nothing about it.
/// </remarks>
public sealed class RelationshipStatusPushTests : IAsyncLifetime
{
    private const int SELF = 10;
    private const int FRIEND = 11;

    private SqliteConnection _conn = null!;
    private DbContextOptions<VortexDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        _conn = new SqliteConnection("Filename=:memory:");
        await _conn.OpenAsync();

        _options = new DbContextOptionsBuilder<VortexDbContext>().UseSqlite(_conn).Options;

        await using VortexDbContext db = new(_options);

        // Not EnsureCreated: every VortexEntity has created_at DatabaseGenerated(Identity) and
        // updated_at Computed, so EF never writes either and relies on the column default the
        // MySQL migrations declare. EnsureCreated emits them NOT NULL with no default, which makes
        // an EF insert of any entity fail on SQLite. Same schema, with those two defaults restored.
        string schema = TimestampDefault.Replace(
            db.Database.GenerateCreateScript(),
            "$1 NOT NULL DEFAULT CURRENT_TIMESTAMP"
        );

        await db.Database.ExecuteSqlRawAsync(schema);
    }

    private static readonly Regex TimestampDefault = new(
        """("(?:created_at|updated_at)"\s+\w+)\s+NOT NULL""",
        RegexOptions.Compiled
    );

    public async Task DisposeAsync() => await _conn.DisposeAsync();

    [Fact]
    public async Task SetRelationshipStatus_PersistsAndPushesTheUpdatedEntryBack()
    {
        DbContextOptions<VortexDbContext> options = _options;
        await SeedFriendshipAsync(options);

        List<IComposer> pushed = [];
        MessengerGrain grain = NewGrain(options, pushed);
        await grain.OnActivateAsync(CancellationToken.None);

        await grain.SetRelationshipStatusAsync(
            FRIEND,
            MessengerFriendRelationType.Two,
            CancellationToken.None
        );

        await using VortexDbContext check = new(options);
        MessengerFriendEntity stored = await check.MessengerFriends.SingleAsync(f =>
            f.PlayerEntityId == SELF && f.FriendPlayerEntityId == FRIEND
        );
        stored.RelationType.Should().Be(MessengerFriendRelationType.Two);

        FriendListUpdateMessageComposer update = pushed
            .OfType<FriendListUpdateMessageComposer>()
            .Should()
            .ContainSingle()
            .Subject;

        FriendListUpdateSnapshot entry = update.Updates.Should().ContainSingle().Subject;
        entry.ActionType.Should().Be(FriendListUpdateActionType.Updated);
        entry.FriendId.Value.Should().Be(FRIEND);
        // Updated, not Added: an Added on an existing entry makes the console list them twice.
        entry.Friend!.RelationshipStatus.Should().Be((short)MessengerFriendRelationType.Two);
    }

    /// <summary>
    /// The wire carries a bare int and the value is written into the short the friend-list entry
    /// serializes. The client only ever offers its four buttons, so anything else is crafted.
    /// </summary>
    [Fact]
    public async Task SetRelationshipStatus_DropsAValueTheClientCouldNotHaveSent()
    {
        DbContextOptions<VortexDbContext> options = _options;
        await SeedFriendshipAsync(options);

        List<IComposer> pushed = [];
        MessengerGrain grain = NewGrain(options, pushed);
        await grain.OnActivateAsync(CancellationToken.None);

        await grain.SetRelationshipStatusAsync(
            FRIEND,
            (MessengerFriendRelationType)99,
            CancellationToken.None
        );

        await using VortexDbContext check = new(options);
        MessengerFriendEntity stored = await check.MessengerFriends.SingleAsync(f =>
            f.PlayerEntityId == SELF && f.FriendPlayerEntityId == FRIEND
        );

        stored.RelationType.Should().Be(MessengerFriendRelationType.Zero);
        pushed.OfType<FriendListUpdateMessageComposer>().Should().BeEmpty();
    }

    [Fact]
    public async Task SetRelationshipStatus_IgnoresSomebodyWhoIsNotAFriend()
    {
        DbContextOptions<VortexDbContext> options = _options;
        await SeedFriendshipAsync(options);

        List<IComposer> pushed = [];
        MessengerGrain grain = NewGrain(options, pushed);
        await grain.OnActivateAsync(CancellationToken.None);

        await grain.SetRelationshipStatusAsync(
            9999,
            MessengerFriendRelationType.One,
            CancellationToken.None
        );

        pushed.OfType<FriendListUpdateMessageComposer>().Should().BeEmpty();
    }

    private static async Task SeedFriendshipAsync(DbContextOptions<VortexDbContext> options)
    {
        await using VortexDbContext seed = new(options);

        PlayerEntity self = NewPlayer(SELF, "tester");
        PlayerEntity friend = NewPlayer(FRIEND, "buddy");

        await seed.Players.AddRangeAsync(self, friend);
        seed.MessengerFriends.Add(
            new MessengerFriendEntity
            {
                Id = 1,
                PlayerEntityId = SELF,
                FriendPlayerEntityId = FRIEND,
                RelationType = MessengerFriendRelationType.Zero,
                PlayerEntity = self,
                FriendPlayerEntity = friend,
            }
        );

        await seed.SaveChangesAsync();
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

    private static MessengerGrain NewGrain(
        DbContextOptions<VortexDbContext> options,
        List<IComposer> pushed
    )
    {
        IPlayerPresenceGrain presence = FakeProxy.Create<IPlayerPresenceGrain>(call =>
        {
            if (call.Method.Name == nameof(IPlayerPresenceGrain.SendComposerAsync))
            {
                pushed.Add((IComposer)call.Args![0]!);
                return Task.CompletedTask;
            }

            return call.Method.Name == nameof(IPlayerPresenceGrain.IsOnlineAsync)
                ? Task.FromResult(false)
                : null;
        });

        IGrainFactory grainFactory = FakeProxy.Create<IGrainFactory>(call =>
            call.Method.Name == nameof(IGrainFactory.GetGrain) ? presence : null
        );

        return GrainActivationContext.CreateWithIntegerKey<MessengerGrain>(
            SELF,
            new TestDbContextFactory(options),
            grainFactory,
            FakeProxy.Create<IEventPublisher>(_ => Task.CompletedTask),
            NullLogger<MessengerGrain>.Instance,
            Options.Create(new MessengerConfig()),
            FakeProxy.Create<ISessionGateway>(_ => null)
        );
    }

    private sealed class TestDbContextFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);
    }
}
