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
using Vortex.Furniture.Providers;
using Vortex.Primitives;
using Vortex.Primitives.Action;
using Vortex.Primitives.Events;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Furniture.Snapshots;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Events;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Object.Furniture.Wall;
using Vortex.Primitives.Rooms.Object.Logic;
using Vortex.Primitives.Rooms.Providers;
using Vortex.Primitives.Rooms.Snapshots;
using Vortex.Primitives.Rooms.Snapshots.Avatars;
using Vortex.Primitives.Rooms.Snapshots.Mapping;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Primitives.Rpg.Characters.Grains;
using Vortex.Primitives.Server.Grains;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Grains;
using Vortex.Rooms.Configuration;
using Vortex.Rooms.Grains;
using Vortex.Rooms.Grains.Systems;
using Vortex.Rooms.Grains.Zones.Triggers;
using Vortex.Rooms.Object.Avatars.Player;
using Vortex.Rooms.Providers;
using Vortex.Rpg.Characters.Zones;
using Vortex.Rpg.Zones;
using Vortex.Rpg.Zones.Rules;
using Vortex.Tests.Support;
using Vortex.Tests.Support.World;

namespace Vortex.Rooms.Tests.Support;

/// <summary>
/// A room grain built outside a silo: an open floor to walk on, the room broadcast swapped for a recorder and room events collected. Shared, because standing a room up
/// is most of the work in any test that needs one.
/// </summary>
internal sealed class RoomHarness
{
    public const int RoomIdValue = 55;
    public const string OwnerName = "Owner";
    public const string OriginalFigure = "hd-180-1";

    /// <summary>Room enough for a walk with a turn in it.</summary>
    private const int MapSize = 12;

    /// <summary>A simulated tick's length. Unused since the bot tick was removed.</summary>
    private const long TickMs = 1_000;

    public static readonly PlayerId Owner = new(101);
    public static readonly PlayerId Stranger = new(202);

    private readonly DbContextOptions<VortexDbContext> _options;

    private long _now;

    private RoomHarness(
        DbContextOptions<VortexDbContext> options,
        bool canManipulate,
        string[] capabilities,
        IZoneConditionEvaluator[] conditions,
        IZoneActionExecutor[] actions
    )
    {
        _options = options;

        Grain = GrainActivationContext.CreateWithIntegerKey<RoomGrain>(
            RoomIdValue,
            ObserveContext,
            [
                new SingleOptionsFactory(options),
                FakeProxy.Create<IFurnitureDefinitionProvider>(call =>
                    call.Method.Name == nameof(IFurnitureDefinitionProvider.TryGetDefinition)
                        ? Definitions.GetValueOrDefault((int)call.Args![0]!)
                        : null
                ),
                // The real one: pure, and a zone's decor states are written through it.
                new StuffDataFactory(),
                Options.Create(new RoomConfig()),
                NullLogger<IRoomGrain>.Instance,
                FakeProxy.Create<IRoomModelProvider>(call =>
                    call.Method.Name == nameof(IRoomModelProvider.CompileCustomModel)
                        ? call.Method.Invoke(Models, call.Args)
                        : null
                ),
                FakeProxy.Create<IRoomItemsProvider>(call =>
                    call.Method.Name == nameof(IRoomItemsProvider.LoadByRoomIdAsync)
                        ? CountItemsLoadAsync()
                        : null
                ),
                FakeProxy.Create<IRoomObjectLogicProvider>(call =>
                    call.Method.Name == nameof(IRoomObjectLogicProvider.CreateLogicInstance)
                        ? LogicFactory?.Invoke()
                        : null
                ),
                FakeProxy.Create<IRoomAvatarProvider>(call =>
                    call.Method.Name == nameof(IRoomAvatarProvider.CreateAvatarFromPlayerSnapshot)
                        ? new RoomPlayerAvatar
                        {
                            ObjectId = (RoomObjectId)call.Args![0]!,
                            PlayerId = ((PlayerSummarySnapshot)call.Args![1]!).PlayerId,
                        }
                        : null
                ),
                FakeProxy.Create<IRoomEventListenerProvider>(_ =>
                    Array.Empty<IRoomEventListener>()
                ),
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
                FakeProxy.Create<IZoneRoomSource>(call =>
                    call.Method.Name == nameof(IZoneRoomSource.FindPublishedRoomAsync)
                        ? ZoneLookup?.Task
                            ?? Task.FromResult<RoomId?>(
                                ZoneRooms.TryGetValue((string)call.Args![0]!, out RoomId room)
                                    ? room
                                    : null
                            )
                        : Task.FromResult(Zone)
                ),
                FakeProxy.Create<IZoneSimulationFactory>(call =>
                    Simulation?.Invoke((IZoneRuntime)call.Args![0]!, (ZoneDocument)call.Args[1]!)
                    ?? NoZoneSimulation.Instance
                ),
                // The character's part of the zone's facts, read from the fake character grain.
                new ZoneRuleRegistry(
                    conditions,
                    actions,
                    [new CharacterFactsSource(BuildGrainFactory())],
                    NullLogger<ZoneRuleRegistry>.Instance
                ),
            ]
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
            AllowBlocking = false,
            PaintWall = string.Empty,
            PaintFloor = string.Empty,
            PaintLandscape = string.Empty,
            Password = string.Empty,
            WorldType = string.Empty,
            HideWalls = false,
            WallThickness = RoomThicknessType.Normal,
            FloorThickness = RoomThicknessType.Normal,
        };

        // A room with no model has no tiles, and an avatar with nowhere to step cannot be seen to walk.
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
            // Build rights in this room, which is what lets somebody move furniture that is not
            // theirs.
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

    /// <summary>Who the grain pushed a composer at, in order.</summary>
    public List<PlayerId> ComposersSentTo { get; } = [];

    /// <summary>The same sends with the composer attached, for a test that asserts what was in it
    /// rather than only that it went out.</summary>
    public List<(PlayerId Target, IComposer Composer)> ComposersSent { get; } = [];

    /// <summary>What the whole room was told, in order.</summary>
    public List<IComposer> BroadcastToRoom { get; } = [];

    /// <summary>The characters the room reads (a target's level and class); none by default.</summary>
    public Dictionary<PlayerId, CharacterSummary> Characters { get; } = [];

    /// <summary>Every character grain throws, as one whose database read failed.</summary>
    public bool CharacterFails { get; set; }

    /// <summary>What each character tells a zone on entry (spec Z1 §4.3); none by default.</summary>
    public Dictionary<PlayerId, CharacterZoneFacts> Facts { get; } = [];

    /// <summary>
    /// Room events as they were published, so a test can see that something fired.
    /// </summary>
    public List<RoomEvent> RoomEvents { get; } = [];

    /// <summary>Everything the room put on the global event bus, in order.</summary>
    public List<IEvent> PublishedEvents { get; } = [];

    /// <summary>Every furniture definition granted to a player, in order.</summary>
    public List<(PlayerId Owner, int DefinitionId)> FurnitureGranted { get; } = [];

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

    /// <summary>The floor items the room reads from its rows when it loads its furniture.</summary>
    public List<IRoomFloorItem> FloorItemsToLoad { get; } = [];

    /// <summary>The published document the zone source answers with; null = an ordinary room.</summary>
    public ZoneDocument? Zone { get; set; }

    /// <summary>The fights a zone runs; none unless the test gives them.</summary>
    public Func<IZoneRuntime, ZoneDocument, IZoneSimulation>? Simulation { get; set; }

    /// <summary>The rooms of the other published zones, by zone key, for passages.</summary>
    public Dictionary<string, RoomId> ZoneRooms { get; } = [];

    /// <summary>When set, every zone lookup answers when this completes: a slow database.</summary>
    public TaskCompletionSource<RoomId?>? ZoneLookup { get; set; }

    /// <summary>The copy the instance directory hands out; null = every copy is taken.</summary>
    public RoomId? InstanceToGive { get; set; }

    /// <summary>Every copy asked for, as (template, group).</summary>
    public List<(RoomId Template, string Group)> InstanceRequests { get; } = [];

    /// <summary>The zone rooms that answer "full" to an announced arrival.</summary>
    public HashSet<RoomId> FullZones { get; } = [];

    /// <summary>Every arrival announced to another zone's room.</summary>
    public List<(RoomId Room, PlayerId Player, string Entry)> ExpectedArrivals { get; } = [];

    /// <summary>The furniture definitions the room can resolve by id.</summary>
    public Dictionary<int, FurnitureDefinitionSnapshot> Definitions { get; } = [];

    /// <summary>How many times the room asked for its furniture rows.</summary>
    public int ItemsProviderCalls { get; private set; }

    /// <summary>What the room handed its write-behind, one entry per call.</summary>
    public List<object?[]> PersistenceBatches { get; } = [];

    private static readonly RoomModelProvider Models = new(
        null!,
        NullLogger<IRoomModelProvider>.Instance
    );

    private Task<(
        IReadOnlyList<IRoomFloorItem>,
        IReadOnlyList<IRoomWallItem>,
        IReadOnlyDictionary<PlayerId, string>
    )> CountItemsLoadAsync()
    {
        ItemsProviderCalls++;

        return Task.FromResult<(
            IReadOnlyList<IRoomFloorItem>,
            IReadOnlyList<IRoomWallItem>,
            IReadOnlyDictionary<PlayerId, string>
        )>((FloorItemsToLoad, [], new Dictionary<PlayerId, string>()));
    }

    /// <summary>A room running <paramref name="zone"/>: the zone is applied the way activation
    /// applies it, then the map is rebuilt from the compiled model.</summary>
    public static async Task<RoomHarness> CreateZoneAsync(
        ZoneDocument zone,
        Func<IZoneRuntime, ZoneDocument, IZoneSimulation>? simulation = null,
        IZoneConditionEvaluator[]? conditions = null,
        IZoneActionExecutor[]? actions = null
    )
    {
        RoomHarness harness = await CreateAsync(conditions: conditions, actions: actions)
            .ConfigureAwait(true);

        harness.Zone = zone;
        harness.Simulation = simulation;
        harness.Definitions[ZoneSamples.Throne] = TestDefinitions.Floor(
            ZoneSamples.Throne,
            "throne",
            1,
            1
        );
        harness.Definitions[ZoneSamples.Poster] = TestDefinitions.Wall(
            ZoneSamples.Poster,
            "poster"
        );
        harness.LogicFactory ??= TestFloorItems.WorkingLogic;

        await harness.Grain.ApplyZoneAsync(CancellationToken.None).ConfigureAwait(true);

        harness.Grain._state.IsMapReady = false;
        await harness
            .Grain.MapModule.EnsureMapBuiltAsync(CancellationToken.None)
            .ConfigureAwait(true);

        return harness;
    }

    /// <summary>How many times the room asked to be deactivated.</summary>
    public int Deactivations { get; private set; }

    private object? ObserveContext(ProxyCall call)
    {
        if (call.Method.Name == "DeactivateOnIdle")
        {
            Deactivations++;
        }

        // Recorded, never answered: the default stub decides what the call returns.
        return null;
    }

    /// <summary>Runs the room's write-behind flush once.</summary>
    public Task FlushItemsAsync() => Grain.FlushDirtyItemsAsync(CancellationToken.None);

    /// <summary>
    /// Server-config integers that differ from the compiled default. Everything else reads its
    /// default, which is what a hotel with no admin overrides runs on.
    /// </summary>
    public Dictionary<string, int> ConfigOverrides { get; } = [];

    /// <param name="capabilities">
    /// The hotel-wide capabilities the permission service answers with, for the grain methods that
    /// gate on a staff power rather than on an in-room controller level.
    /// </param>
    /// <param name="conditions">Zone conditions a module would declare (spec Z1 §4.2).</param>
    /// <param name="actions">Zone actions a module would declare.</param>
    public static Task<RoomHarness> CreateAsync(
        bool canManipulate = true,
        string[]? capabilities = null,
        IZoneConditionEvaluator[]? conditions = null,
        IZoneActionExecutor[]? actions = null
    )
    {
        DbContextOptions<VortexDbContext> options = new DbContextOptionsBuilder<VortexDbContext>()
            .UseInMemoryDatabase($"room-{Guid.NewGuid():N}")
            .Options;

        return Task.FromResult(
            new RoomHarness(
                options,
                canManipulate,
                capabilities ?? [],
                conditions ?? [],
                actions ?? []
            )
        );
    }

    public VortexDbContext NewDbContext() => new(_options);

    public ActionContext ContextFor(PlayerId playerId) =>
        new(ActionOrigin.Player, default, playerId, new RoomId(RoomIdValue));

    /// <summary>
    /// Runs the avatar tick.
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

    /// <summary>Closes a tile, which is what furniture standing on one amounts to for a walker.</summary>
    public void BlockTile(int x, int y) =>
        Grain._state.TileFlags[Grain.MapModule.ToIdx(x, y)] = RoomTileFlags.Closed;

    /// <summary>
    /// Stands the owner in the room wearing a given look, for a test that reads the avatar on
    /// screen rather than the stored figure.
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
    /// standing here".
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

    /// <summary>An avatar standing on a tile.</summary>
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
    /// What the event publisher hands back, so a test can make handlers slow or broken. Null (the
    /// default) means the proxy's own completed task.
    /// </summary>
    public Task? PublishResult { get; set; }

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

            // An item carries its owner's name, which the room resolves through the directory
            // when it is not already cached.
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
                return FakeProxy.Create<IRoomPersistenceGrain>(inner =>
                {
                    PersistenceBatches.Add(inner.Args ?? []);

                    return null;
                });
            }

            // An empty batch snapshot (and the caller's own fallback for single reads) gives the
            // compiled defaults, which is what a hotel with no admin overrides runs on anyway.
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

            // A summary for whichever player is asked about: a year-old account under the owner's
            // name and look.
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
                        _ => null,
                    }
                );
            }

            if (call.Method.GetGenericArguments()[0] == typeof(IRoomZone))
            {
                RoomId destination = new((int)(long)call.Args![0]!);

                return FakeProxy.Create<IRoomZone>(inner =>
                {
                    if (inner.Method.Name == nameof(IRoomZone.ExpectArrivalAtAsync))
                    {
                        ExpectedArrivals.Add(
                            (
                                destination,
                                (PlayerId)inner.Args![0]!,
                                $"{inner.Args![1]},{inner.Args![2]}"
                            )
                        );

                        return null;
                    }

                    if (FullZones.Contains(destination))
                    {
                        return Task.FromResult(false);
                    }

                    ExpectedArrivals.Add(
                        (destination, (PlayerId)inner.Args![0]!, (string)inner.Args![1]!)
                    );

                    return Task.FromResult(true);
                });
            }

            // The dungeon copies: whatever the test says is free, and who asked.
            if (call.Method.GetGenericArguments()[0] == typeof(IZoneInstanceGrain))
            {
                return FakeProxy.Create<IZoneInstanceGrain>(inner =>
                {
                    if (inner.Method.Name != nameof(IZoneInstanceGrain.EnterAsync))
                    {
                        return null;
                    }

                    InstanceRequests.Add(((RoomId)inner.Args![0]!, (string)inner.Args![1]!));

                    return Task.FromResult(InstanceToGive);
                });
            }

            if (call.Method.GetGenericArguments()[0] == typeof(IPlayerCharacterGrain))
            {
                PlayerId character = new((int)(long)call.Args![0]!);

                return FakeProxy.Create<IPlayerCharacterGrain>(inner =>
                    inner.Method.Name switch
                    {
                        nameof(IPlayerCharacterGrain.GetSummaryAsync) => CharacterFails
                            ? Task.FromException<CharacterSummary?>(
                                new InvalidOperationException("character grain down")
                            )
                            : Task.FromResult(Characters.GetValueOrDefault(character)),
                        nameof(IPlayerCharacterGrain.GetZoneFactsAsync) => CharacterFails
                            ? Task.FromException<CharacterZoneFacts?>(
                                new InvalidOperationException("character grain down")
                            )
                            : Task.FromResult(Facts.GetValueOrDefault(character)),
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
