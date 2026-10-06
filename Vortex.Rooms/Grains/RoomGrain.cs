using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Runtime;
using Orleans.Streams;
using Vortex.Database.Context;
using Vortex.Database.Entities.Groups;
using Vortex.Database.Entities.Room;
using Vortex.Events.Registry;
using Vortex.Logging;
using Vortex.Logging.Extensions;
using Vortex.Primitives;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Events;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Groups.Enums;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Orleans.Snapshots.Room.Settings;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Pets.Providers;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Events;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.Rooms.Providers;
using Vortex.Primitives.Rooms.Snapshots;
using Vortex.Rooms.Configuration;
using Vortex.Rooms.Games.Presentation;
using Vortex.Rooms.Games.Progression;
using Vortex.Rooms.Games.Runtime;
using Vortex.Rooms.Grains.Modules;
using Vortex.Rooms.Grains.Systems;
using Vortex.Rooms.Providers;

namespace Vortex.Rooms.Grains;

public sealed partial class RoomGrain : Grain, IRoomGrain
{
    /// <summary>How many consecutive failures of one tick step pass between log lines.</summary>
    private const int TickFailureLogInterval = 200;

    /// <summary>Consecutive failure count per tick step, cleared as soon as the step succeeds.</summary>
    private readonly Dictionary<string, int> _tickStepFailures = [];

    /// <summary>The room clock. One-shot, re-armed to the next boundary at the end of every tick.</summary>
    private IGrainTimer? _roomTimer;

    internal readonly IRoomAvatarProvider _avatarProvider;
    internal readonly IDbContextFactory<VortexDbContext> _dbCtxFactory;
    internal readonly IFurnitureDefinitionProvider _definitionProvider;
    internal readonly IStuffDataFactory _stuffDataFactory;
    internal readonly IEventPublisher _events;
    internal readonly ICancellableEventPublisher _cancellableEvents;
    internal readonly IGrainFactory _grainFactory;
    internal readonly IRoomItemsProvider _itemsLoader;
    internal readonly ILogger<IRoomGrain> _logger;
    internal readonly IRoomObjectLogicProvider _logicProvider;
    internal readonly IRoomModerationStore _moderationStore;
    internal readonly IVortexMetrics _metrics;
    internal readonly IPermissionService _permissionService;
    internal readonly IPetLevelProvider _petLevelProvider;
    internal readonly IPetCommandProvider _petCommandProvider;
    internal readonly IPetVocalProvider _petVocalProvider;
    internal readonly RoomConfig _roomConfig;
    internal readonly IRoomModelProvider _roomModelProvider;

    internal readonly RoomLiveState _state;

    /// <summary>
    /// Where a room's value-moving operations are recorded — today, the prize a consumed furni owes
    /// (<see cref="GrantConsumedPrizeAsync"/>).
    /// </summary>
    internal readonly ICommerceJournal _commerceJournal;

    /// <summary>The hotel-wide word filter, asked before the room's own on every player chat line.</summary>
    internal readonly IWordFilter _wordFilter;

    internal readonly IRoomEventListenerProvider _eventListenerProvider;
    public readonly RoomActionModule ActionModule;
    public readonly RoomAvatarModule AvatarModule;
    public readonly RoomHandItemModule HandItemModule;
    public readonly RoomAvatarTickSystem AvatarTickSystem;
    public readonly RoomChatSystem ChatSystem;

    /// <summary>The room's one game coordinator. Every game the room hosts is inside it, and nothing
    /// outside it names a game.</summary>
    public readonly RoomGameRuntime GameRuntime;
    public readonly GameTimerSystem GameTimers;
    public readonly GameScoreboardPresenter ScoreboardPresenter;

    public readonly RoomEventModule EventModule;
    public readonly RoomFurniModule FurniModule;
    public readonly RoomMapModule MapModule;
    public readonly RoomObjectModule ObjectModule;

    public readonly RoomPathingSystem PathingSystem;
    public readonly RoomPetSystem PetSystem;
    public readonly RoomBotSystem BotSystem;
    public readonly RoomRollerSystem RollerSystem;
    public readonly RoomSecurityModule SecurityModule;
    public readonly RoomModerationSystem ModerationSystem;
    public readonly RoomRaidProtectionSystem RaidProtectionSystem;
    public readonly RoomTradingSystem TradingSystem;

    internal IAsyncStream<RoomOutbound> _roomOutbound = default!;

    public RoomGrain(
        IDbContextFactory<VortexDbContext> dbCtxFactory,
        IFurnitureDefinitionProvider definitionProvider,
        IStuffDataFactory stuffDataFactory,
        IOptions<RoomConfig> roomConfig,
        ILogger<IRoomGrain> logger,
        IRoomModelProvider roomModelProvider,
        IRoomItemsProvider itemsLoader,
        IRoomObjectLogicProvider logicProvider,
        IRoomAvatarProvider avatarProvider,
        IRoomEventListenerProvider eventListenerProvider,
        IGrainFactory grainFactory,
        IEventPublisher events,
        ICancellableEventPublisher cancellableEvents,
        IPermissionService permissionService,
        IVortexMetrics metrics,
        IRoomModerationStore moderationStore,
        IPetLevelProvider petLevelProvider,
        IPetCommandProvider petCommandProvider,
        IPetVocalProvider petVocalProvider,
        IRoomGameProvider gameProvider,
        ICommerceJournal commerceJournal,
        // Last on purpose: GrainActivationContext.CreateWithIntegerKey takes params object[], so a
        // dependency inserted anywhere else compiles cleanly in every test that builds this grain
        // and then fails at activation. Appending keeps that failure at the end of the list, where
        // a reader of the test looks for it.
        IWordFilter wordFilter
    )
    {
        _dbCtxFactory = dbCtxFactory;
        _definitionProvider = definitionProvider;
        _stuffDataFactory = stuffDataFactory;
        _roomConfig = roomConfig.Value;
        _logger = logger;
        _roomModelProvider = roomModelProvider;
        _itemsLoader = itemsLoader;
        _logicProvider = logicProvider;
        _avatarProvider = avatarProvider;
        _eventListenerProvider = eventListenerProvider;
        _grainFactory = grainFactory;
        _events = events;
        _cancellableEvents = cancellableEvents;
        _permissionService = permissionService;
        _metrics = metrics;
        _moderationStore = moderationStore;
        _petLevelProvider = petLevelProvider;
        _petCommandProvider = petCommandProvider;
        _petVocalProvider = petVocalProvider;
        _commerceJournal = commerceJournal;
        _wordFilter = wordFilter;

        _state = new RoomLiveState { RoomId = (RoomId)this.GetPrimaryKeyLong() };
        PathingSystem = new RoomPathingSystem(this);
        EventModule = new RoomEventModule(this);
        SecurityModule = new RoomSecurityModule(this);
        MapModule = new RoomMapModule(this);
        ObjectModule = new RoomObjectModule(this);
        AvatarModule = new RoomAvatarModule(this);
        HandItemModule = new RoomHandItemModule(this);
        FurniModule = new RoomFurniModule(this);
        ActionModule = new RoomActionModule(this);

        AvatarTickSystem = new RoomAvatarTickSystem(this);
        PetSystem = new RoomPetSystem(this);
        BotSystem = new RoomBotSystem(this);
        RollerSystem = new RoomRollerSystem(this);
        ChatSystem = new RoomChatSystem(this);
        GameRuntime = new RoomGameRuntime(this);
        GameTimers = new GameTimerSystem(this);
        ScoreboardPresenter = new GameScoreboardPresenter(this);
        ModerationSystem = new RoomModerationSystem(this);
        RaidProtectionSystem = new RoomRaidProtectionSystem(this);
        TradingSystem = new RoomTradingSystem(this);

        // Every game the room hosts arrives from the provider, which holds whatever any scanned
        // assembly marked [RoomGame]. There is no list of games in this file and there must not be
        // one: that is the property that makes adding a game a new folder rather than an edit here,
        // in the timer furni and in the tick loop.
        gameProvider.AttachGamesTo(GameRuntime);
        GameRuntime.AddSink(new GameDiagnosticsSink(_logger));
        // One sink for every game's scoring, rather than progression plumbing inside each game.
        GameRuntime.AddSink(new GameProgressSink(RoomId.Value, PublishDetached));

        EventModule.Register(RollerSystem);
        // The scoreboard adapter paints every game's score displays from the game events, so no game
        // refreshes a board by hand.
        EventModule.Register(ScoreboardPresenter);
    }

    public RoomId RoomId => _state.RoomId;

    public Task DeactivateRoomAsync()
    {
        DeactivateOnIdle();

        return Task.CompletedTask;
    }

    public Task DelayRoomDeactivationAsync()
    {
        DelayDeactivation(TimeSpan.FromMilliseconds(_roomConfig.RoomDeactivationDelayMs));

        return Task.CompletedTask;
    }

    public async Task EnsureRoomActiveAsync(CancellationToken ct)
    {
        await DelayRoomDeactivationAsync().ConfigureAwait(true);

        await MapModule.EnsureMapBuiltAsync(ct);
        await FurniModule.EnsureFurniLoadedAsync(ct);
        await PetSystem.EnsurePetsLoadedAsync(ct);
    }

    /// <summary>
    /// Population is stamped 0 when the snapshot is built from the entity and is never written
    /// back, so the stored value would report an empty room forever - the guest-room card, which
    /// serialises this snapshot straight to the wire, showed "0 users" for every room. The live
    /// count sits on this same grain, so it costs nothing to stamp it on the way out.
    /// </summary>
    public Task<RoomSnapshot> GetSnapshotAsync()
    {
        return Task.FromResult(
            _state.RoomSnapshot with
            {
                Population = _state.AvatarsByPlayerId.Count,
            }
        );
    }

    public async Task<RoomSummarySnapshot> GetSummaryAsync()
    {
        int population = await GetRoomPopulationAsync();

        return new RoomSummarySnapshot
        {
            RoomId = _state.RoomSnapshot.RoomId,
            Name = _state.RoomSnapshot.Name,
            Description = _state.RoomSnapshot.Description,
            OwnerId = _state.RoomSnapshot.OwnerId,
            OwnerName = _state.RoomSnapshot.OwnerName,
            Population = population,
            LastUpdatedUtc = DateTime.UtcNow,
        };
    }

    public Task<int> GetRoomPopulationAsync() => Task.FromResult(_state.AvatarsByPlayerId.Count);

    public Task<ImmutableArray<KeyValuePair<string, string>>> GetRoomPropertiesAsync() =>
        Task.FromResult(_state.RoomProperties.ToImmutableArray());

    public Task PublishRoomEventAsync(RoomEvent evt, CancellationToken ct)
    {
        return EventModule.PublishAsync(evt, ct);
    }

    public Task SendComposerToRoomAsync(IComposer composer)
    {
        return _roomOutbound.OnNextAsync(
            new RoomOutbound { RoomId = _state.RoomId, Composer = composer }
        );
    }

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        if (_state.EpochMs == 0)
        {
            long now = NowMs();

            _state.EpochMs = now;
            _state.NextAvatarBoundaryMs = AlignToNextBoundary(now, _roomConfig.AvatarTickMs);
            _state.NextPetBoundaryMs = AlignToNextBoundary(now, _roomConfig.Pet.TickMs);
            _state.NextBotBoundaryMs = now;
            _state.NextRollerBoundaryMs = AlignToNextBoundary(now, _roomConfig.RollerTickMs);
        }

        await HydrateRoomStateAsync(ct);
        await HydrateModerationStateAsync(ct);

        // Contributed listeners are attached here rather than in the constructor: they come from
        // scanned assemblies, and handing a half-built grain to outside code is how a plugin ends up
        // reading state that does not exist yet. They go after the roller/scoreboard systems,
        // so an outside listener can slow a room down but never pre-empt what the room does with its
        // own events.
        foreach (IRoomEventListener listener in _eventListenerProvider.BuildListenersForRoom(this))
        {
            EventModule.Register(listener);
        }

        using (_metrics.MeasureRoomDirectoryCall(nameof(IRoomDirectoryGrain.UpsertActiveRoomAsync)))
        {
            await _grainFactory.GetRoomDirectoryGrain().UpsertActiveRoomAsync(_state.RoomSnapshot);
        }

        IStreamProvider? provider = this.GetStreamProvider(
            OrleansStreamProviders.ROOM_STREAM_PROVIDER
        );

        StreamId streamId = StreamId.Create(
            OrleansStreamNames.ROOM_STREAM,
            this.GetPrimaryKeyLong()
        );

        _roomOutbound = provider.GetStream<RoomOutbound>(streamId);

        // One-shot, re-armed at the end of every tick to the next epoch-aligned RoomTickMs
        // boundary. A *periodic* grain timer measures its period from the end of the previous
        // callback, so the phase drifted by the tick body's own duration each cycle and the
        // avatar/roller boundaries -- all multiples of RoomTickMs from EpochMs -- were
        // crossed late by a varying amount. What a client sees of that is choppy walking.
        _roomTimer = this.RegisterGrainTimer<object?>(
            async (state, ct) =>
            {
                try
                {
                    await RunRoomTickAsync(ct);
                }
                finally
                {
                    // In a finally: a tick that throws must not stop the room's clock for good.
                    RearmRoomTimer();
                }
            },
            null,
            TimeSpan.FromMilliseconds(_roomConfig.RoomTickMs),
            Timeout.InfiniteTimeSpan
        );
    }

    private void RearmRoomTimer()
    {
        long now = NowMs();

        _roomTimer?.Change(
            TimeSpan.FromMilliseconds(AdvanceBoundaryPast(now, _roomConfig.RoomTickMs) - now),
            Timeout.InfiniteTimeSpan
        );
    }

    private async Task RunRoomTickAsync(CancellationToken ct)
    {
        // Read once: at 20 ticks a second per room, a disabled metrics stack must cost this
        // boolean and nothing else -- no timestamps taken, no elapsed time computed.
        bool measured = _metrics.Enabled;
        long tickStartedAt = measured ? Stopwatch.GetTimestamp() : 0L;

        long now = NowMs();

        // Each step is isolated. Run bare, one throw -- a single malformed item, one
        // pet with a broken path -- aborted the whole tick, and since the two flushes are
        // last, the room also stopped persisting furniture and tile changes for as long as
        // the fault lasted.
        await RunTickStepAsync("avatars", () => AvatarTickSystem.ProcessAvatarsAsync(now, ct));
        await RunTickStepAsync("pets", () => PetSystem.ProcessPetsAsync(now, ct));
        await RunTickStepAsync("bots", () => BotSystem.ProcessBotsAsync(now, ct));
        await RunTickStepAsync("rollers", () => RollerSystem.ProcessRollersAsync(now, ct));
        await RunTickStepAsync("game-timer", () => GameTimers.ProcessAsync(now, ct));
        // One step for every game the room hosts. The runtime isolates each game's failure and skips
        // the ones with nothing to do, so a room with no match running does no per-game work at all.
        await RunTickStepAsync("games", () => GameRuntime.TickAsync(now, ct));

        await RunTickStepAsync("doorbell", () => ProcessDoorbellTimeoutsAsync(now, ct));
        await RunTickStepAsync(
            "raid-protection",
            () => RaidProtectionSystem.TickAsync(DateTime.UtcNow, ct)
        );
        await RunTickStepAsync("flush-tiles", () => FlushDirtyTilesAsync(ct));
        await RunTickStepAsync("flush-items", () => FlushDirtyItemsAsync(ct));

        if (measured)
        {
            _metrics.RoomTickCompleted(Stopwatch.GetElapsedTime(tickStartedAt).TotalMilliseconds);
        }
    }

    /// <summary>
    /// Runs one tick step, keeping its failure to itself. A step that throws must not take the rest
    /// of the tick down with it -- least of all the two flushes at the end, which are what actually
    /// persist the room.
    /// </summary>
    private async Task RunTickStepAsync(string step, Func<Task> stepAsync)
    {
        bool measured = _metrics.Enabled;
        long startedAt = measured ? Stopwatch.GetTimestamp() : 0L;

        try
        {
            await stepAsync().ConfigureAwait(true);

            _tickStepFailures.Remove(step);
        }
        catch (OperationCanceledException)
        {
            // The grain is going away; not a step failure, and not a duration worth recording -- the
            // step was cut short rather than finished.
            throw;
        }
        catch (Exception ex)
        {
            // At 50ms a permanently broken step would emit twenty lines a second, per room, for as
            // long as the fault lasts, so only the first failure and then one in every
            // TickFailureLogInterval are written. The count doubles as "how long has this been
            // broken", which is the part worth reading.
            int failures = _tickStepFailures.GetValueOrDefault(step) + 1;
            _tickStepFailures[step] = failures;

            if (failures == 1 || failures % TickFailureLogInterval == 0)
            {
                _logger.LogError(
                    ex,
                    "Room tick step {Step} failed in room {RoomId} ({FailureCount} consecutive); the rest of the tick still ran.",
                    step,
                    _state.RoomId.Value,
                    failures
                );
            }
        }

        // A step that threw is still a step that consumed tick budget, so it is timed like any other;
        // only the cancellation path above escapes, and it does so by rethrowing.
        if (measured)
        {
            _metrics.RoomTickStepCompleted(
                step,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds
            );
        }
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct)
    {
        try
        {
            // Games first: a room unload destroys its game runtime, and tearing every match down
            // through its own cleanup is what guarantees no timer, effect or queued piece of work
            // outlives the activation.
            await GameRuntime.ShutdownAsync(ct);

            await FlushDirtyItemsAsync(ct);
            await PetSystem.FlushDirtyPetsAsync(ct);

            using (
                _metrics.MeasureRoomDirectoryCall(nameof(IRoomDirectoryGrain.RemoveActiveRoomAsync))
            )
            {
                await _grainFactory.GetRoomDirectoryGrain().RemoveActiveRoomAsync(_state.RoomId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanly deactivate room {RoomId}.", _state.RoomId);
        }
    }

    private async Task HydrateRoomStateAsync(CancellationToken ct)
    {
        VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct);

        try
        {
            RoomEntity entity =
                await dbCtx
                    .Rooms.AsNoTracking()
                    .Include(e => e.GroupEntity)
                    .Include(e => e.PlayerEntity)
                    .SingleOrDefaultAsync(e => e.Id == _state.RoomId.Value, ct)
                ?? throw new VortexException(VortexErrorCodeEnum.RoomNotFound);

            _state.Model = _roomModelProvider.GetModelById(entity.RoomModelEntityId);

            _state.RoomSnapshot = new RoomSnapshot
            {
                RoomId = entity.Id,
                Name = entity.Name ?? string.Empty,
                Description = entity.Description ?? string.Empty,
                OwnerId = entity.PlayerEntityId,
                OwnerName = entity.PlayerEntity?.Name ?? string.Empty,
                LeaveOnDoorTile = entity.LeaveOnDoorTile,
                IdleSleepEnabled = entity.IdleSleepEnabled,
                IdleSleepTimeoutSeconds = entity.IdleSleepTimeoutSeconds,
                IdleAutokickEnabled = entity.IdleAutokickEnabled,
                IdleAutokickTimeoutSeconds = entity.IdleAutokickTimeoutSeconds,
                MuteAllPets = entity.MuteAllPets,
                Population = 0,
                DoorMode = entity.DoorMode,
                PlayersMax = entity.PlayersMax,
                MaxVisitorsLimit = _roomConfig.MaxVisitorsLimit,
                TradeType = entity.TradeType,
                Score = entity.Score,
                Ranking = entity.Ranking,
                CategoryId = entity.NavigatorCategoryEntityId ?? -1,
                Tags = RoomTagMapper.ToTags(entity.Tag1, entity.Tag2),
                AllowBlocking = entity.AllowBlocking,
                AllowPets = entity.AllowPets,
                AllowPetsEat = entity.AllowPetsEat,
                GroupId = entity.GroupEntityId,
                GroupName = entity.GroupEntity?.Name,
                GroupBadge = entity.GroupEntity?.Badge,
                PaintWall = entity.PaintWall ?? string.Empty,
                PaintFloor = entity.PaintFloor ?? string.Empty,
                PaintLandscape = entity.PaintLandscape ?? string.Empty,
                Password = entity.Password ?? string.Empty,
                ModSettings = new ModSettingsSnapshot
                {
                    WhoCanMute = entity.MuteType,
                    WhoCanKick = entity.KickType,
                    WhoCanBan = entity.BanType,
                },
                ChatSettings = new ChatSettingsSnapshot
                {
                    ChatMode = entity.ChatModeType,
                    BubbleWidth = entity.ChatBubbleType,
                    ScrollSpeed = entity.ChatSpeedType,
                    FullHearRange = entity.ChatDistance,
                    FloodSensitivity = entity.ChatFloodType,
                },
                WorldType = _state.Model.Name,
                HideWalls = entity.HideWalls,
                WallThickness = entity.ThicknessWall,
                FloorThickness = entity.ThicknessFloor,
                WallHeight = entity.WallHeight,
                LastUpdatedUtc = DateTime.UtcNow,
            };

            _state.RoomProperties[RoomPropertyType.WALLPAPER] = DecorationOrDefault(
                entity.PaintWall
            );
            _state.RoomProperties[RoomPropertyType.FLOOR] = DecorationOrDefault(entity.PaintFloor);
            _state.RoomProperties[RoomPropertyType.LANDSCAPE] = DecorationOrDefault(
                entity.PaintLandscape
            );
            _state.RoomProperties[RoomPropertyType.LANDSCAPEANIM] = "0";

            List<int> rightsPlayerIds = await dbCtx
                .RoomRights.AsNoTracking()
                .Where(r => r.RoomEntityId == entity.Id && r.DeletedAt == null)
                .Select(r => r.PlayerEntityId)
                .ToListAsync(ct)
                .ConfigureAwait(true);

            _state.PlayerIdsWithRights.Clear();

            foreach (int playerId in rightsPlayerIds)
            {
                _state.PlayerIdsWithRights.Add(playerId);
            }

            List<string> filterWords = await dbCtx
                .RoomFilterWords.AsNoTracking()
                .Where(w => w.RoomEntityId == entity.Id && w.DeletedAt == null)
                .Select(w => w.Word)
                .ToListAsync(ct)
                .ConfigureAwait(true);

            _state.FilterWords.Clear();

            foreach (string word in filterWords)
            {
                _state.FilterWords.Add(word);
            }

            // Only worth a config read when there is something to mask. Most rooms have an empty
            // filter, and ApplyFilterWords returns before it ever looks at the replacement.
            if (_state.FilterWords.Count > 0)
            {
                _state.FilterReplacement =
                    await _grainFactory
                        .GetServerConfigGrain()
                        .GetValueAsync(RoomFilterConfig.ReplacementKey)
                        .ConfigureAwait(true)
                    ?? RoomFilterConfig.ReplacementDefault;
            }

            await HydrateGroupMembershipAsync(dbCtx, entity.GroupEntityId, ct).ConfigureAwait(true);
        }
        finally
        {
            await dbCtx.DisposeAsync();
        }
    }

    /// <summary>
    /// A room property the client can read but that no decoration has been applied to yet. The
    /// client treats "0" as "the default surface"; an empty string would render as a missing asset.
    /// </summary>
    private static string DecorationOrDefault(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "0" : value;

    /// <summary>
    /// Loads the owning guild's membership roster and decoration policy into live state so
    /// <see cref="Modules.RoomSecurityModule"/> can resolve group controller levels without a DB hit
    /// per check. A no-op (and a full reset) for rooms that are not a guild base.
    /// </summary>
    private async Task HydrateGroupMembershipAsync(
        VortexDbContext dbCtx,
        int? groupId,
        CancellationToken ct
    )
    {
        _state.GroupMemberRanks.Clear();
        _state.GroupAdminOnlyDecoration = false;

        if (groupId is not int id)
        {
            return;
        }

        _state.GroupAdminOnlyDecoration = await dbCtx
            .Groups.AsNoTracking()
            .Where(g => g.Id == id && g.DeletedAt == null)
            .Select(g => g.AdminOnlyDecoration)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(true);

        // Two columns, not the row. The roster is read for one question -- what rank does this
        // player hold here -- and a guild base for a large guild was pulling every column of every
        // membership row, foreign keys and timestamps included, into the room's activation
        // (PERS-HYD-014). It is still every member: trimming the list would quietly take a rank
        // away from whoever fell off the end.
        List<GroupMemberRankRow> members = await dbCtx
            .GroupMembers.AsNoTracking()
            .Where(m => m.GroupEntityId == id && m.DeletedAt == null)
            .Select(m => new GroupMemberRankRow(m.PlayerEntityId, m.Rank))
            .ToListAsync(ct)
            .ConfigureAwait(true);

        foreach (GroupMemberRankRow member in members)
        {
            _state.GroupMemberRanks[member.PlayerId] = member.Rank;
        }
    }

    /// <summary>
    /// Publishes an event without holding the room's turn open for its handlers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>EventRegistry</c> runs handlers in parallel, but <c>PublishAsync</c> is awaited, so the
    /// room waits for the slowest of them. On the furniture events that is a cross-grain call per
    /// subscriber — quest progress, achievement progress, the daily task — and it happens on every
    /// click of a build session: placing a sofa held the room, and everyone standing in it, until
    /// the placer's own grains had answered (INFRA-EVENT-058).
    /// </para>
    /// <para>
    /// Only for events that <b>no handler answers back into this room with</b>. The three furniture
    /// ones qualify: their subscribers write forensics into an in-memory buffer and call the
    /// <em>player's</em> grains, never the room's, so nothing detached here can re-enter the
    /// activation it was published from. That is the question INFRA-AWAIT-059 leaves open in
    /// general, and it has to be answered per event rather than assumed — which is why this is a
    /// named method used at five sites, not the default for <c>_events</c>.
    /// </para>
    /// <para>
    /// Published on no cancellation token: the caller's token belongs to a turn that is about to
    /// end, and cancelling with it would drop the event exactly when the request completed normally.
    /// </para>
    /// </remarks>
    internal void PublishDetached(IEvent evt) =>
        _events
            .PublishAsync(evt, CancellationToken.None)
            .LogAndForget(
                _logger,
                "Detached publication of {EventType} from room {RoomId} failed.",
                evt.GetType().Name,
                _state.RoomId.Value
            );

    internal long NowMs()
    {
        return (long)(Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency);
    }

    internal long AlignToNextBoundary(long now, int offset)
    {
        long delta = now - _state.EpochMs;
        long mod = delta % offset;

        return mod == 0 ? now : now + (offset - mod);
    }

    /// <summary>
    /// The first tick boundary strictly after <paramref name="now"/>, on the same epoch grid a tick
    /// clock walks.
    /// <para>
    /// Every tick system used to catch up by stepping — <c>while (now >= next) next += tick</c> —
    /// which is exact and cheap for the millisecond drift a running room sees. It is neither when
    /// the gap is large: a room that was paused, a host that slept, a clock that jumped, and the
    /// step count becomes millions, spent inside the grain's single turn with the whole room
    /// waiting behind it. Arriving at the same answer in one step removes that cliff without
    /// changing where the boundaries fall.
    /// </para>
    /// </summary>
    internal long AdvanceBoundaryPast(long now, int tickMs)
    {
        long aligned = AlignToNextBoundary(now, tickMs);

        // AlignToNextBoundary answers "now" when now is itself a boundary; a clock has to move past
        // the boundary it just fired, or the same tick runs again.
        return aligned == now ? now + tickMs : aligned;
    }

    /// <summary>The two columns of a membership row the room actually reads.</summary>
    private readonly record struct GroupMemberRankRow(int PlayerId, GroupMemberRank Rank);

    private async Task HydrateModerationStateAsync(CancellationToken ct)
    {
        IReadOnlyList<RoomMuteRecord> activeMutes = await _moderationStore.GetActiveMutesAsync(
            _state.RoomId.Value,
            ct
        );

        _state.MuteExpiresUtc.Clear();

        foreach (RoomMuteRecord mute in activeMutes)
        {
            _state.MuteExpiresUtc[mute.PlayerId] = mute.ExpiresUtc;
        }
    }
}
