using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Streams;
using Vortex.Database.Context;
using Vortex.Database.Entities.Room;
using Vortex.Events.Registry;
using Vortex.Primitives;
using Vortex.Primitives.Action;
using Vortex.Primitives.Bots;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Events;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Navigator.Enums;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Orleans.Snapshots.Room.Settings;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Pets.Providers;
using Vortex.Primitives.Pets.Snapshots;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Events;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Object.Logic;
using Vortex.Primitives.Rooms.Providers;
using Vortex.Primitives.Rooms.Snapshots;
using Vortex.Primitives.Rooms.Snapshots.Avatars;
using Vortex.Primitives.Rooms.Snapshots.Mapping;
using Vortex.Primitives.Server.Grains;
using Vortex.Rooms.Configuration;
using Vortex.Rooms.Grains;
using Vortex.Rooms.Grains.Systems;
using Vortex.Rooms.Object.Avatars.Player;
using Vortex.Tests.Support;

namespace Vortex.Rooms.Tests.Support;

/// <summary>
/// A room grain built outside a silo: an open floor to walk on, one bot standing in it, the room
/// broadcast swapped for a recorder and room events collected. Shared, because standing a room up
/// is most of the work in any test that needs one.
/// </summary>
internal sealed class RoomHarness
{
    public const int RoomIdValue = 55;
    public const int PlacedBotId = 7;
    public const string OwnerName = "Owner";
    public const string OriginalFigure = "hd-180-1";
    public const string BotName = "Bartender";

    /// <summary>Where the seeded bot stands, clear of the walls it might otherwise path into.</summary>
    public const int BotStartX = 3;
    public const int BotStartY = 4;

    /// <summary>Wider than the wander radius, so a bot in the middle has somewhere to go.</summary>
    private const int MapSize = 12;

    /// <summary>The room's bot tick, which is what a simulated tick has to advance past.</summary>
    private const long TickMs = 1_000;

    public static readonly PlayerId Owner = new(101);
    public static readonly PlayerId Stranger = new(202);

    private readonly DbContextOptions<VortexDbContext> _options;

    private long _now;

    private RoomHarness(
        DbContextOptions<VortexDbContext> options,
        bool canManipulate,
        string[] capabilities
    )
    {
        _options = options;

        Grain = GrainActivationContext.CreateWithIntegerKey<RoomGrain>(
            RoomIdValue,
            new SingleOptionsFactory(options),
            FakeProxy.Create<IFurnitureDefinitionProvider>(_ => null),
            FakeProxy.Create<IStuffDataFactory>(_ => null),
            Options.Create(new RoomConfig()),
            NullLogger<IRoomGrain>.Instance,
            FakeProxy.Create<IRoomModelProvider>(_ => null),
            FakeProxy.Create<IRoomItemsProvider>(_ => null),
            FakeProxy.Create<IRoomObjectLogicProvider>(call =>
                call.Method.Name == nameof(IRoomObjectLogicProvider.CreateLogicInstance)
                    ? LogicFactory?.Invoke()
                    : null
            ),
            FakeProxy.Create<IRoomAvatarProvider>(_ => null),
            FakeProxy.Create<IRoomEventListenerProvider>(_ => Array.Empty<IRoomEventListener>()),
            BuildGrainFactory(),
            FakeProxy.Create<IEventPublisher>(call =>
            {
                if (call.Method.Name == nameof(IEventPublisher.PublishAsync))
                {
                    PublishedEvents.Add((IEvent)call.Args![0]!);
                }

                // VSTHRD003: handing back a task the test made on purpose -- a slow or broken
                // publisher is the whole point of PublishResult, and nothing here awaits it.
#pragma warning disable VSTHRD003
                return PublishResult;
#pragma warning restore VSTHRD003
            }),
            FakeProxy.Create<ICancellableEventPublisher>(call =>
            {
                IEvent published = (IEvent)call.Args![0]!;

                PublishedEvents.Add(published);

                return Task.FromResult(
                    new EventContext
                    {
                        CorrelationId = string.Empty,
                        Cancel = CancelWhen(published),
                    }
                );
            }),
            BuildPermissionService(capabilities),
            FakeProxy.Create<IVortexMetrics>(_ => null),
            // The real store, on the same in-memory database as everything else here. It was a
            // no-op proxy, which meant a ban could be "applied" in a test and land nowhere — and
            // raid protection bans without a human actor, so that was the only thing left to assert.
            // Plain EF, nothing this provider cannot run.
            new RoomModerationStore(
                new SingleOptionsFactory(options),
                NullLogger<RoomModerationStore>.Instance
            ),
            BuildPetLevelProvider(),
            FakeProxy.Create<IPetCommandProvider>(call =>
                call.Method.Name == nameof(IPetCommandProvider.GetCommandConfig)
                    ? PetCommands((int)call.Args![0]!, (int)call.Args![1]!)
                    : null
            ),
            FakeProxy.Create<IPetVocalProvider>(_ => null),
            TestRoomGames.Provider(),
            BuildCommerceJournal(),
            RoomGrainStubs.PassThroughWordFilter()
        );

        // The security module reads the room's own snapshot to decide who owns the place, so a
        // grain built outside a silo needs one before any rights question can be asked.
        Grain._state.RoomSnapshot = new RoomSnapshot
        {
            RoomId = new RoomId(RoomIdValue),
            Name = "Test room",
            Description = string.Empty,
            OwnerId = Owner,
            OwnerName = OwnerName,
            Population = 0,
            LastUpdatedUtc = DateTime.UnixEpoch,
            DoorMode = RoomDoorModeType.Open,
            PlayersMax = 25,
            TradeType = RoomTradeModeType.Disabled,
            Score = 0,
            Ranking = 0,
            CategoryId = -1,
            Tags = [],
            AllowBlocking = false,
            AllowPets = true,
            AllowPetsEat = false,
            PaintWall = string.Empty,
            PaintFloor = string.Empty,
            PaintLandscape = string.Empty,
            StaffPick = false,
            Password = string.Empty,
            ModSettings = new ModSettingsSnapshot
            {
                WhoCanMute = ModSettingType.Owner,
                WhoCanKick = ModSettingType.Owner,
                WhoCanBan = ModSettingType.Owner,
            },
            ChatSettings = new ChatSettingsSnapshot
            {
                ChatMode = ChatModeType.FreeFlow,
                BubbleWidth = ChatBubbleWidthType.Normal,
                ScrollSpeed = ChatScrollSpeedType.Normal,
                FullHearRange = 50,
                FloodSensitivity = ChatFloodSensitivityType.Normal,
            },
            WorldType = string.Empty,
            HideWalls = false,
            WallThickness = RoomThicknessType.Normal,
            FloorThickness = RoomThicknessType.Normal,
            MaxVisitorsLimit = 25,
        };

        // A room with no model has no tiles, and a bot with nowhere to step cannot be seen to walk.
        // An open square is the smallest map that lets walking be observed at all.
        Grain._state.Model = new RoomModelSnapshot
        {
            Id = 1,
            Name = "test",
            Model = "test",
            DoorX = 0,
            DoorY = 0,
            DoorRotation = Rotation.South,
            Width = MapSize,
            Height = MapSize,
            Size = MapSize * MapSize,
            BaseHeights = [.. Enumerable.Repeat(Altitude.Zero, MapSize * MapSize)],
            BaseFlags = [.. Enumerable.Repeat(RoomTileFlags.Walkable, MapSize * MapSize)],
        };

        // All six per-tile arrays, not the two the map used to be read through. A real room gets
        // them together from EnsureMapBuiltAsync(); a harness that allocates a subset models a room
        // that cannot exist, and the first production read of a missing one throws an
        // IndexOutOfRangeException from somewhere that looks unrelated to the test.
        Grain._state.TileHeights = [.. Enumerable.Repeat(Altitude.Zero, MapSize * MapSize)];
        Grain._state.TileFlags = [.. Enumerable.Repeat(RoomTileFlags.Walkable, MapSize * MapSize)];
        Grain._state.TileEncodedHeights = new short[MapSize * MapSize];
        Grain._state.TileHighestFloorItems =
        [
            .. Enumerable.Repeat((RoomObjectId)(-1), MapSize * MapSize),
        ];
        Grain._state.TileFloorStacks =
        [
            .. Enumerable.Range(0, MapSize * MapSize).Select(_ => new HashSet<RoomObjectId>()),
        ];
        Grain._state.TileAvatarStacks =
        [
            .. Enumerable.Range(0, MapSize * MapSize).Select(_ => new HashSet<RoomObjectId>()),
        ];

        if (canManipulate)
        {
            // Build rights in this room, which is what lets somebody clear up furniture — and now
            // bots — that is not theirs.
            Grain._state.PlayerIdsWithRights.Add(Stranger);
        }

        // The room broadcast normally rides an Orleans stream, which does not exist outside a silo.
        // Swapping it for a recorder keeps the code under test unchanged and makes what the room was
        // told an assertable thing rather than a crash.
        Grain._roomOutbound = FakeProxy.Create<IAsyncStream<RoomOutbound>>(call =>
        {
            if (call.Method.Name == nameof(IAsyncStream<RoomOutbound>.OnNextAsync))
            {
                BroadcastToRoom.Add(((RoomOutbound)call.Args![0]!).Composer);
            }

            return null;
        });

        Grain.EventModule.Register(new EventRecorder(RoomEvents));

        // The simulated clock starts where the room's own does. Anything the room stamps for itself
        // — a hand item's expiry, say — is measured against that clock, so a test ticking from zero
        // would be permanently in its past.
        _now = Grain.NowMs();
    }

    public RoomGrain Grain { get; }

    public RoomBotSystem BotSystem => Grain.BotSystem;

    /// <summary>Who the grain pushed an inventory composer at — the point of the ownership tests.</summary>
    public List<PlayerId> ComposersSentTo { get; } = [];

    /// <summary>The same sends with the composer attached, for a test that asserts what was in it
    /// rather than only that it went out.</summary>
    public List<(PlayerId Target, IComposer Composer)> ComposersSent { get; } = [];

    /// <summary>What the whole room was told, in order.</summary>
    public List<IComposer> BroadcastToRoom { get; } = [];

    /// <summary>
    /// Room events as they were published, so a test can see that something fired.
    /// </summary>
    public List<RoomEvent> RoomEvents { get; } = [];

    /// <summary>Everything the room put on the global event bus, in order.</summary>
    public List<IEvent> PublishedEvents { get; } = [];

    /// <summary>Stands in for the behaviours a plugin would register: return true for an event and
    /// the room sees that publish come back cancelled.</summary>
    public Func<IEvent, bool> CancelWhen { get; set; } = _ => false;

    /// <summary>
    /// What <c>CreateLogicInstance</c> hands back. Null — the default — covers every test that puts
    /// items straight into the live state and never attaches one; a test going through the real
    /// attach path sets it before placing, because an item whose logic comes back null fails the
    /// attach rather than reaching the map.
    /// </summary>
    public Func<IRoomObjectLogic>? LogicFactory { get; set; }

    /// <summary>
    /// The pet commands the room knows, by pet type and command id. None by default, which is a
    /// room whose pets refuse every order as unknown.
    /// </summary>
    public Func<int, int, PetCommandEntry?> PetCommands { get; set; } = (_, _) => null;

    /// <summary>
    /// Server-config integers that differ from the compiled default. Everything else reads its
    /// default, which is what a hotel with no admin overrides runs on.
    /// </summary>
    public Dictionary<string, int> ConfigOverrides { get; } = [];

    /// <param name="capabilities">
    /// The hotel-wide capabilities the permission service answers with, for the grain methods that
    /// gate on a staff power rather than on an in-room controller level.
    /// </param>
    public static async Task<RoomHarness> CreateAsync(
        int placedBotId = PlacedBotId,
        bool canManipulate = true,
        string[]? capabilities = null
    )
    {
        DbContextOptions<VortexDbContext> options = new DbContextOptionsBuilder<VortexDbContext>()
            .UseInMemoryDatabase($"room-bots-{Guid.NewGuid():N}")
            .Options;

        await using (VortexDbContext seed = new(options))
        {
            seed.Bots.Add(
                new BotEntity
                {
                    Id = placedBotId,
                    OwnerPlayerEntityId = Owner.Value,
                    RoomEntityId = RoomIdValue,
                    Name = BotName,
                    Figure = OriginalFigure,
                    Gender = AvatarGenderType.Male,
                    X = BotStartX,
                    Y = BotStartY,
                }
            );

            await seed.SaveChangesAsync().ConfigureAwait(true);
        }

        return new RoomHarness(options, canManipulate, capabilities ?? []);
    }

    public VortexDbContext NewDbContext() => new(_options);

    public ActionContext ContextFor(PlayerId playerId) =>
        new(ActionOrigin.Player, default, playerId, new RoomId(RoomIdValue));

    /// <summary>How long the test room holds a hand item, so a test can tick past it.</summary>
    public const int HandItemDurationMs = 30_000;

    /// <summary>
    /// Runs the avatar tick, which is a different clock from the bot one — hand items expire on it.
    /// </summary>
    public async Task TickAvatarsAsync(int ticks = 1)
    {
        for (int tick = 0; tick < ticks; tick++)
        {
            _now += Grain._roomConfig.AvatarTickMs;

            await Grain
                .AvatarTickSystem.ProcessAvatarsAsync(_now, CancellationToken.None)
                .ConfigureAwait(true);
        }
    }

    /// <summary>Jumps the avatar clock past a duration and ticks once, which is what expiry needs.</summary>
    public async Task TickAvatarsPastAsync(long durationMs)
    {
        _now += durationMs + Grain._roomConfig.AvatarTickMs;

        await Grain
            .AvatarTickSystem.ProcessAvatarsAsync(_now, CancellationToken.None)
            .ConfigureAwait(true);
    }

    /// <summary>Runs the bot tick, each call standing for one second of room time.</summary>
    public async Task TickAsync(int ticks = 1)
    {
        for (int tick = 0; tick < ticks; tick++)
        {
            _now += TickMs;

            await Grain.ProcessBotsForTestAsync(_now).ConfigureAwait(true);
        }
    }

    /// <summary>The bot as the room would draw it, which is where its position is observable.</summary>
    public async Task<RoomBotAvatarSnapshot> ReadBotAvatarAsync()
    {
        ImmutableArray<RoomAvatarSnapshot> avatars = await Grain
            .GetPlacedBotAvatarSnapshotsAsync(CancellationToken.None)
            .ConfigureAwait(true);

        return avatars.OfType<RoomBotAvatarSnapshot>().Single();
    }

    public Task EnableWanderAsync() =>
        Grain.SetBotSkillAsync(
            ContextFor(Owner),
            PlacedBotId,
            BotSkillId.RandomWalk,
            string.Empty,
            CancellationToken.None
        );

    /// <summary>Closes a tile, which is what furniture standing on one amounts to for a bot.</summary>
    public void BlockTile(int x, int y) =>
        Grain._state.TileFlags[Grain.MapModule.ToIdx(x, y)] = RoomTileFlags.Closed;

    /// <summary>
    /// Stands the owner in the room wearing a given look. Dressing a bot up reads the avatar on
    /// screen rather than the stored figure, so there has to be one to read.
    /// </summary>
    public void PutOwnerInRoomWearing(string figure, AvatarGenderType gender) =>
        PutPlayerInRoom(Owner, 0, 0, figure, gender);

    /// <summary>
    /// A real avatar rather than a stub, for the tests that need one to carry state — holding
    /// something, being walked about — rather than merely to stand somewhere.
    /// </summary>
    public RoomPlayerAvatar PutRealPlayerInRoom(PlayerId playerId, int x, int y)
    {
        RoomObjectId objectId = new(playerId.Value);

        RoomPlayerAvatar avatar = new() { ObjectId = objectId, PlayerId = playerId };
        avatar.SetPosition(x, y);

        Grain._state.AvatarsByPlayerId[playerId] = objectId;
        Grain._state.AvatarsByObjectId[objectId] = avatar;

        return avatar;
    }

    /// <summary>
    /// The same, but also on the tile's avatar stack — which is what the room means by "somebody is
    /// standing here", and what a blast radius or a rolling ball asks.
    /// </summary>
    public RoomPlayerAvatar PutRealPlayerOnTile(PlayerId playerId, int x, int y)
    {
        RoomPlayerAvatar avatar = PutRealPlayerInRoom(playerId, x, y);
        int tileIdx = Grain.MapModule.ToIdx(x, y);

        if (Grain.MapModule.InBounds(tileIdx))
        {
            Grain._state.TileAvatarStacks[tileIdx].Add(avatar.ObjectId);
        }

        return avatar;
    }

    /// <summary>
    /// A pet standing on a tile. Placed straight into the room's live state rather than through the
    /// pet system, because what is under test is what happens to it, not how it got there.
    /// </summary>
    public async Task PutPetInRoomAsync(int petId, int x, int y)
    {
        // Load first, on an empty table: the pet system clears its cache the first time it is
        // asked, so a pet placed before that would be wiped by the very call under test.
        await Grain.PetSystem.EnsurePetsLoadedAsync(CancellationToken.None).ConfigureAwait(true);

        Grain._state.PetsById[petId] = new PetSnapshot
        {
            PetId = petId,
            OwnerId = Owner,
            RoomId = RoomIdValue,
            Name = "Rex",
            Type = 0,
            Race = 0,
            Color = "FFFFFF",
            Gender = AvatarGenderType.Male,
            Level = 1,
            Experience = 0,
            Energy = 100,
            Nutrition = 100,
            Respect = 0,
            X = x,
            Y = y,
            Z = 0,
            Direction = Rotation.South,
        };
    }

    /// <summary>An avatar standing on a tile, which is what a following bot walks towards.</summary>
    public void PutPlayerInRoom(
        PlayerId playerId,
        int x,
        int y,
        string figure = "hd-1-1",
        AvatarGenderType gender = AvatarGenderType.Male
    )
    {
        RoomObjectId objectId = new(playerId.Value);

        Grain._state.AvatarsByPlayerId[playerId] = objectId;
        Grain._state.AvatarsByObjectId[objectId] = FakeProxy.Create<IRoomPlayer>(call =>
            call.Method.Name switch
            {
                $"get_{nameof(IRoomAvatar.Figure)}" => figure,
                $"get_{nameof(IRoomPlayer.Gender)}" => gender,
                $"get_{nameof(IRoomAvatar.X)}" => x,
                $"get_{nameof(IRoomAvatar.Y)}" => y,
                $"get_{nameof(IRoomAvatar.ObjectId)}" => objectId,
                _ => null,
            }
        );
    }

    /// <summary>
    /// Caps at Habbo's full-bar hundred. The default stub answers zero, which silently clamps every
    /// fed pet back to empty and makes feeding look broken when it is not.
    /// </summary>
    private static IPetLevelProvider BuildPetLevelProvider() =>
        FakeProxy.Create<IPetLevelProvider>(call =>
            call.Method.Name.StartsWith("Get", StringComparison.Ordinal)
            && call.Method.ReturnType == typeof(int)
                ? 100
                : null
        );

    /// <summary>
    /// What the event publisher hands back, so a test can make handlers slow or broken. Null (the
    /// default) means the proxy's own completed task.
    /// </summary>
    public Task? PublishResult { get; set; }

    /// <summary>Every state the room moved a commerce operation through, in order.</summary>
    public List<CommerceOperationState> JournalStates { get; } = [];

    /// <summary>Makes the journal itself fail, which a payout must survive: the furniture behind a
    /// prize is already destroyed by the time it is written.</summary>
    public bool JournalThrows { get; set; }

    private ICommerceJournal BuildCommerceJournal() =>
        FakeProxy.Create<ICommerceJournal>(call =>
        {
            if (JournalThrows)
            {
                throw new InvalidOperationException("journal unavailable");
            }

            if (call.Method.Name == nameof(ICommerceJournal.TransitionAsync))
            {
                JournalStates.Add((CommerceOperationState)call.Args![1]!);
            }

            return Task.CompletedTask;
        });

    /// <summary>
    /// A staff-capability-free player, so the manipulate check falls through to the room's own
    /// rights list — which is what the tests vary.
    /// </summary>
    private static IPermissionService BuildPermissionService(string[] capabilities) =>
        FakeProxy.Create<IPermissionService>(call =>
            call.Method.Name == nameof(IPermissionService.ResolveForPlayerAsync)
                ? Task.FromResult(new PermissionSet([], capabilities))
                : null
        );

    private IGrainFactory BuildGrainFactory() =>
        FakeProxy.Create<IGrainFactory>(call =>
        {
            if (!call.Method.IsGenericMethod)
            {
                return null;
            }

            // The bot avatar carries its owner's name, which the room resolves through the
            // directory when it is not already cached.
            if (call.Method.GetGenericArguments()[0] == typeof(IPlayerDirectoryGrain))
            {
                return FakeProxy.Create<IPlayerDirectoryGrain>(inner =>
                    inner.Method.Name == nameof(IPlayerDirectoryGrain.GetPlayerNameAsync)
                        ? Task.FromResult(OwnerName)
                        : null
                );
            }

            // The room's write-behind. Nothing here asserts on what was queued — the persistence
            // grain has a suite of its own — but every detach hands it a snapshot on its way out,
            // and a null grain there is an exception in the middle of one.
            if (call.Method.GetGenericArguments()[0] == typeof(IRoomPersistenceGrain))
            {
                return FakeProxy.Create<IRoomPersistenceGrain>(_ => null);
            }

            // Games read their live balance from the server config on every round start. An empty
            // batch snapshot (and the caller's own fallback for single reads) gives them the compiled
            // defaults, which is what a hotel with no admin overrides runs on anyway.
            if (call.Method.GetGenericArguments()[0] == typeof(IServerConfigGrain))
            {
                return FakeProxy.Create<IServerConfigGrain>(inner =>
                    inner.Method.Name switch
                    {
                        nameof(IServerConfigGrain.GetIntAsync) => Task.FromResult(
                            ConfigOverrides.TryGetValue((string)inner.Args![0]!, out int value)
                                ? value
                                : (int)inner.Args![1]!
                        ),
                        nameof(IServerConfigGrain.GetManyAsync) => Task.FromResult(
                            ImmutableDictionary<string, string>.Empty
                        ),
                        _ => null,
                    }
                );
            }

            // The two answers pet care asks of the scratcher: how old the account is (the scratch
            // has a minimum age) and whether the day's allowance is spent. An old-enough account
            // with allowance left, so a test about the scratch is not a test about its gates.
            if (call.Method.GetGenericArguments()[0] == typeof(IPlayerGrain))
            {
                PlayerId scratcher = new((int)(long)call.Args![0]!);

                return FakeProxy.Create<IPlayerGrain>(inner =>
                    inner.Method.Name switch
                    {
                        nameof(IPlayerGrain.GetSummaryAsync) => Task.FromResult(
                            new PlayerSummarySnapshot
                            {
                                PlayerId = scratcher,
                                Name = OwnerName,
                                Motto = string.Empty,
                                Figure = OriginalFigure,
                                Gender = AvatarGenderType.Male,
                                AchievementScore = 0,
                                CreatedAt = DateTime.UtcNow.AddYears(-1),
                            }
                        ),
                        nameof(IPlayerGrain.TryGivePetRespectAsync) => Task.FromResult(true),
                        _ => null,
                    }
                );
            }

            if (call.Method.GetGenericArguments()[0] != typeof(IPlayerPresenceGrain))
            {
                return null;
            }

            PlayerId target = new((int)(long)call.Args![0]!);

            return FakeProxy.Create<IPlayerPresenceGrain>(inner =>
            {
                if (inner.Method.Name == nameof(IPlayerPresenceGrain.SendComposerAsync))
                {
                    ComposersSentTo.Add(target);

                    // Both overloads land here: one composer, or the params array.
                    foreach (object? arg in inner.Args ?? [])
                    {
                        if (arg is IComposer one)
                        {
                            ComposersSent.Add((target, one));
                        }
                        else if (arg is IComposer[] many)
                        {
                            foreach (IComposer composer in many)
                            {
                                ComposersSent.Add((target, composer));
                            }
                        }
                    }
                }

                return null;
            });
        });

    private sealed class SingleOptionsFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);
    }

    /// <summary>Records every room event, standing in for a listener a plugin would register.</summary>
    private sealed class EventRecorder(List<RoomEvent> events) : IRoomEventListener
    {
        public Task OnRoomEventAsync(RoomEvent evt, CancellationToken ct)
        {
            events.Add(evt);

            return Task.CompletedTask;
        }
    }
}
