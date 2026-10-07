using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Vortex.Primitives.Action;
using Vortex.Primitives.Fishing;
using Vortex.Primitives.Fishing.Grains;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Furniture.Snapshots;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Snapshots.Avatars;
using Vortex.Primitives.Rooms.Snapshots.Furniture;
using Vortex.Protocol.Messages.Outgoing.Fishing;

namespace Vortex.Fishing.Grains;

/// <summary>
/// One player's running fishing session.
/// </summary>
/// <remarks>
/// <para>
/// Reconstructed from Habbo Origins, which has no client dump — see the client's
/// <c>docs/vortex-original/fishing.md</c>.
/// </para>
/// <para>
/// <strong>A session, not a cast per fish.</strong> The client sends one <c>StartFishing</c> and
/// afterwards only listens: this grain arms a timer, announces each shadow, resolves each catch and
/// stops when the spot runs dry. Nothing in that loop waits on the client, which is the point —
/// Origins has the avatar fish unattended.
/// </para>
/// <para>
/// The timer is deliberately one-shot and re-armed each step rather than periodic: a step's delay is
/// rolled, and a periodic timer would also fire while the previous step was still resolving.
/// </para>
/// </remarks>
internal sealed class FishingSessionGrain(
    IGrainFactory grainFactory,
    IFurnitureDefinitionProvider definitionProvider,
    ILogger<FishingSessionGrain> logger
) : Grain, IFishingSessionGrain
{
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly IFurnitureDefinitionProvider _definitionProvider = definitionProvider;
    private readonly ILogger<FishingSessionGrain> _logger = logger;

    /// <summary>
    /// How many tiles from the spot a player may stand and still cast. One means the eight
    /// neighbouring tiles, since the spot's own tile cannot be stood on.
    /// </summary>
    private const int MaxCastDistance = 1;

    /// <summary>
    /// The spot furni's splash animation. Built by <c>scripts/origins/build-fishing-spots.py</c>,
    /// which gives every spot two: 0 the idle water and 1 the splash over it.
    /// </summary>
    private const int SplashState = 1;

    private IGrainTimer? _timer;

    private RoomId _roomId;
    private RoomObjectId _spotObjectId;
    private FishingZoneSnapshot? _zone;
    private int _stockRemaining;
    private int _catches;
    private int _sightingId;

    /// <summary>The catch the current shadow will resolve into, rolled when the shadow appears.</summary>
    private FishSpeciesSnapshot? _pendingSpecies;
    private bool _pendingTriggersHookHavoc;

    /// <summary>The live Hook Havoc attempt, or zero when none is. One runs at a time.</summary>
    private int _attemptId;
    private int _attemptSeed;
    private FishSpeciesSnapshot? _attemptSpecies;

    private PlayerId PlayerId => new((int)this.GetPrimaryKeyLong());

    private bool IsRunning => _zone is not null;

    public async Task StartAsync(RoomId roomId, RoomObjectId spotObjectId, CancellationToken ct)
    {
        if (IsRunning)
        {
            // Not an error the player caused — the client disables its own button — but the check is
            // what stops two timers running against one stock.
            await SendErrorAsync(FishingErrorCode.TooSoon, ct).ConfigureAwait(true);

            return;
        }

        FishingZoneSnapshot? zone = await ResolveZoneAsync(roomId, spotObjectId, ct)
            .ConfigureAwait(true);

        if (zone is null)
        {
            await SendErrorAsync(FishingErrorCode.NotASpot, ct).ConfigureAwait(true);

            return;
        }

        if (!await IsWithinReachAsync(roomId, spotObjectId, ct).ConfigureAwait(true))
        {
            await SendErrorAsync(FishingErrorCode.TooFarAway, ct).ConfigureAwait(true);

            return;
        }

        FishingPlayerStateSnapshot state = await _grainFactory
            .GetFishingPlayerGrain(PlayerId)
            .GetStateAsync(0, ct)
            .ConfigureAwait(true);

        if (state.FishingLevel < zone.RequiredLevel)
        {
            // The level the zone wants, so the client can name it. It has no way of working this
            // out for itself: the refusal is what stops it ever learning which zone the spot is in.
            await SendErrorAsync(FishingErrorCode.LevelTooLow, ct, zone.RequiredLevel)
                .ConfigureAwait(true);

            return;
        }

        if (state.DailyCap > 0 && state.CurrencyEarnedToday >= state.DailyCap)
        {
            await SendErrorAsync(FishingErrorCode.DailyCapReached, ct).ConfigureAwait(true);

            return;
        }

        _roomId = roomId;
        _spotObjectId = spotObjectId;
        _zone = zone;
        _catches = 0;
        // "One fish or several" — the stock is rolled per session, so a player cannot tell from the
        // outside how much is left, which is what makes relocating feel like a decision.
        _stockRemaining = Random.Shared.Next(
            Math.Max(1, zone.MinCatches),
            Math.Max(1, zone.MaxCatches) + 1
        );

        await ShowRodAsync(true, ct).ConfigureAwait(true);
        await ArmNextSightingAsync(ct).ConfigureAwait(true);
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (!IsRunning)
        {
            return;
        }

        await ShowRodAsync(false, ct).ConfigureAwait(true);

        EndSession();

        // The client already knows it asked to stop, but the push is what settles a stop that raced
        // a depletion: both end with the same message and the same catch count.
        await SendAsync(
                new VortexFishingSpotDepletedMessageComposer
                {
                    SpotItemId = _spotObjectId.Value,
                    Catches = _catches,
                },
                ct
            )
            .ConfigureAwait(true);
    }

    public async Task AbandonAsync(CancellationToken ct)
    {
        if (!IsRunning)
        {
            return;
        }

        _logger.LogDebug(
            "Dropping the stale fishing session player {PlayerId} left open in room {RoomId}",
            PlayerId,
            _roomId
        );

        // The rod is cleared before the state, because it needs the room this session ran in.
        await ShowRodAsync(false, ct).ConfigureAwait(true);

        EndSession();
    }

    public async Task SubmitHookHavocAsync(int[] timeline, CancellationToken ct)
    {
        if (_attemptId == 0 || _attemptSpecies is null)
        {
            // An attempt that already expired, or one that never existed. Refusing rather than
            // scoring it is what stops a replayed timeline from being banked twice.
            await SendErrorAsync(FishingErrorCode.UnknownSighting, ct).ConfigureAwait(true);

            return;
        }

        FishingSettingsSnapshot settings = await _grainFactory
            .GetFishingDefinitionsGrain()
            .GetSettingsAsync(ct)
            .ConfigureAwait(true);

        bool won = HookHavocSimulation.Replay(
            timeline,
            _attemptSeed,
            settings.HookHavocDurationMs,
            settings.HookHavocFillRate,
            settings.HookHavocTolerance
        );

        int attemptId = _attemptId;
        FishSpeciesSnapshot species = _attemptSpecies;

        _attemptId = 0;
        _attemptSpecies = null;

        if (!won)
        {
            // Failure costs nothing in Origins and fishing resumes immediately, so this is an
            // ordinary outcome with zero rewards rather than an error.
            await SendAsync(
                    new VortexHookHavocResultMessageComposer
                    {
                        AttemptId = attemptId,
                        Won = false,
                        SpeciesId = 0,
                        XpGained = 0,
                        CurrencyGained = 0,
                        TrophyHandItemId = 0,
                    },
                    ct
                )
                .ConfigureAwait(true);

            await ContinueOrDepleteAsync(ct).ConfigureAwait(true);

            return;
        }

        FishingCatchOutcome outcome = await BankCatchAsync(species, golden: true, ct)
            .ConfigureAwait(true);

        await SendAsync(
                new VortexHookHavocResultMessageComposer
                {
                    AttemptId = attemptId,
                    Won = true,
                    SpeciesId = species.Id,
                    XpGained = outcome.XpGranted,
                    CurrencyGained = outcome.CurrencyGranted,
                    TrophyHandItemId = settings.HookHavocTrophyHandItemId,
                },
                ct
            )
            .ConfigureAwait(true);

        await ContinueOrDepleteAsync(ct).ConfigureAwait(true);
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct)
    {
        // A session is in-memory state; leaving the timer behind would keep the activation alive and
        // go on fishing for a player who is no longer there.
        EndSession();

        await base.OnDeactivateAsync(reason, ct).ConfigureAwait(true);
    }

    /// <summary>
    /// Resolves the clicked furniture to a fishing zone, or null when it is not a spot.
    /// </summary>
    /// <remarks>
    /// Done here rather than in the handler because it is the check that decides whether a player may
    /// fish at all: the item must really be in the room they are in, and its class must really be a
    /// zone. A handler that resolved it and handed the answer down would be a handler the client
    /// could lie to.
    /// </remarks>
    /// <summary>
    /// Whether the player is standing next to the spot they clicked.
    /// </summary>
    /// <remarks>
    /// The client sends a furni id and nothing else, so without this any spot in the room could be
    /// fished from anywhere in it — which is what shipped, and what a player noticed immediately.
    /// <para>
    /// Chebyshev distance, not Euclidean: a diagonal neighbour is adjacent on a Habbo tile grid, and
    /// the spot itself is <c>canstandon: false</c>, so the eight tiles around it are the whole of
    /// what counts. A spot bigger than one tile would want its footprint measured instead; every one
    /// this hotel ships is 1x1.
    /// </para>
    /// </remarks>
    private async Task<bool> IsWithinReachAsync(
        RoomId roomId,
        RoomObjectId spotObjectId,
        CancellationToken ct
    )
    {
        RoomItemSnapshot? item = await _grainFactory
            .GetRoomFurni(roomId)
            .GetItemSnapshotByIdAsync(spotObjectId, ct)
            .ConfigureAwait(true);

        if (item is null)
        {
            return false;
        }

        ImmutableArray<RoomAvatarSnapshot> avatars = await _grainFactory
            .GetRoomAvatars(roomId)
            .GetAllAvatarSnapshotsAsync(ct)
            .ConfigureAwait(true);

        foreach (RoomAvatarSnapshot avatar in avatars)
        {
            // A player avatar carries its player id in WebId; bots and pets carry their own ids
            // there, so the type has to be checked or a bot with a colliding id would pass.
            if (avatar.AvatarType != RoomObjectType.Player || avatar.WebId != PlayerId.Value)
            {
                continue;
            }

            return Math.Max(Math.Abs(avatar.X - item.X), Math.Abs(avatar.Y - item.Y))
                <= MaxCastDistance;
        }

        // Not in the room at all.
        return false;
    }

    private async Task<FishingZoneSnapshot?> ResolveZoneAsync(
        RoomId roomId,
        RoomObjectId spotObjectId,
        CancellationToken ct
    )
    {
        RoomItemSnapshot? item = await _grainFactory
            .GetRoomFurni(roomId)
            .GetItemSnapshotByIdAsync(spotObjectId, ct)
            .ConfigureAwait(true);

        if (item is null)
        {
            return null;
        }

        FurnitureDefinitionSnapshot? definition = _definitionProvider.TryGetDefinition(
            item.DefinitionId
        );

        if (definition is null)
        {
            return null;
        }

        return await _grainFactory
            .GetFishingDefinitionsGrain()
            .GetZoneForFurniClassAsync(definition.Name, ct)
            .ConfigureAwait(true);
    }

    /// <summary>Waits a rolled interval, then announces the next shadow.</summary>
    private async Task ArmNextSightingAsync(CancellationToken ct)
    {
        FishingSettingsSnapshot settings = await _grainFactory
            .GetFishingDefinitionsGrain()
            .GetSettingsAsync(ct)
            .ConfigureAwait(true);

        int delay = Random.Shared.Next(
            settings.MinSightingDelayMs,
            Math.Max(settings.MinSightingDelayMs, settings.MaxSightingDelayMs) + 1
        );

        ArmTimer(static (self, tickCt) => ((FishingSessionGrain)self!).SightAsync(tickCt), delay);
    }

    /// <summary>
    /// Picks what is about to bite, announces the shadow, and arms the resolution.
    /// </summary>
    /// <remarks>
    /// The species is chosen <em>before</em> the shadow is drawn but is not sent with it: a sighting
    /// names no species precisely so a client cannot filter for rare ones and stop fishing when a
    /// common one is coming. Only <c>Golden</c> travels, because a Golden Fish is visible in the
    /// water in Origins.
    /// </remarks>
    private async Task SightAsync(CancellationToken ct)
    {
        if (!IsRunning)
        {
            return;
        }

        // The player is gone. Checked here rather than left to the send below, which cannot see it:
        // pushing to an offline player is a no-op, not a throw, so the session would fish on forever
        // for somebody who disconnected — re-arming its timer, holding the grain activated, and
        // refusing their next cast with "already fishing" when they came back.
        if (
            !await _grainFactory
                .GetPlayerPresenceGrain(PlayerId)
                .IsOnlineAsync(ct)
                .ConfigureAwait(true)
        )
        {
            await AbandonAsync(ct).ConfigureAwait(true);

            return;
        }

        // The player walked off. Start is not the only moment reach matters: a session runs on its
        // own timer for as long as the stock lasts, so without this it keeps landing fish from the
        // far side of the room. Checked on every step rather than only on the walk packet, because
        // a walk is not the only way to move — a wired teleport, a push and a roller all move an
        // avatar without one, and all of them have to end the session the same way.
        if (!await IsWithinReachAsync(_roomId, _spotObjectId, ct).ConfigureAwait(true))
        {
            await StopAsync(ct).ConfigureAwait(true);

            return;
        }

        FishingDefinitionsSnapshot definitions = await _grainFactory
            .GetFishingDefinitionsGrain()
            .GetDefinitionsAsync(ct)
            .ConfigureAwait(true);
        FishingPlayerStateSnapshot state = await _grainFactory
            .GetFishingPlayerGrain(PlayerId)
            .GetStateAsync(_catches, ct)
            .ConfigureAwait(true);

        FishSpeciesSnapshot? species = RollSpecies(
            definitions,
            state.FishingLevel,
            DateTime.UtcNow
        );

        if (species is null)
        {
            // Nothing is in season, in the hour, or within the player's level. Not an error — an
            // empty table is a pond with nothing in it, and the honest answer is that it ran dry.
            _logger.LogDebug(
                "No fishing species available in zone {ZoneId} for player {PlayerId} right now",
                _zone!.Id,
                PlayerId
            );

            await DepleteAsync(ct).ConfigureAwait(true);

            return;
        }

        FishingRodLevelSnapshot? rod = RodFor(definitions, state.RodQuality);
        bool frenzy = IsFrenzyRunning(definitions.Settings, DateTime.UtcNow);

        // Every catch triggers Hook Havoc during a frenzy — the two descriptions in circulation
        // ("only Golden Fish" and "every catch is Hook Havoc") are the same statement, because
        // winning Hook Havoc is how a Golden Fish is caught.
        _pendingTriggersHookHavoc =
            frenzy || Random.Shared.Next(1000) < (rod?.HookHavocChance ?? 0);
        _pendingSpecies = species;
        _sightingId++;

        await SendAsync(
                new VortexFishSightedMessageComposer
                {
                    SightingId = _sightingId,
                    SpotItemId = _spotObjectId.Value,
                    Golden = _pendingTriggersHookHavoc,
                    DurationMs = definitions.Settings.SightingDurationMs,
                },
                ct
            )
            .ConfigureAwait(true);

        ArmTimer(
            static (self, tickCt) => ((FishingSessionGrain)self!).ResolveAsync(tickCt),
            definitions.Settings.SightingDurationMs
        );
    }

    /// <summary>
    /// Decides whether the fish was landed, and banks it if so.
    /// </summary>
    /// <remarks>
    /// An escape sends nothing. There is no minigame on an ordinary catch, so a miss is not something
    /// the player did — the shadow simply goes, and the next one is armed. Announcing every miss
    /// would turn a background activity into a stream of bad news.
    /// </remarks>
    private async Task ResolveAsync(CancellationToken ct)
    {
        if (!IsRunning || _pendingSpecies is null)
        {
            return;
        }

        FishSpeciesSnapshot species = _pendingSpecies;
        bool triggersHookHavoc = _pendingTriggersHookHavoc;

        _pendingSpecies = null;

        FishingDefinitionsSnapshot definitions = await _grainFactory
            .GetFishingDefinitionsGrain()
            .GetDefinitionsAsync(ct)
            .ConfigureAwait(true);
        FishingPlayerStateSnapshot state = await _grainFactory
            .GetFishingPlayerGrain(PlayerId)
            .GetStateAsync(_catches, ct)
            .ConfigureAwait(true);

        FishingRodLevelSnapshot? rod = RodFor(definitions, state.RodQuality);

        if (!RollCatch(species, rod, definitions.Settings, _catches))
        {
            await ContinueOrDepleteAsync(ct).ConfigureAwait(true);

            return;
        }

        if (triggersHookHavoc)
        {
            await StartHookHavocAsync(species, definitions.Settings, ct).ConfigureAwait(true);

            return;
        }

        FishingCatchOutcome outcome = await BankCatchAsync(species, golden: false, ct)
            .ConfigureAwait(true);

        await SplashAsync(ct).ConfigureAwait(true);
        await ContinueOrDepleteAsync(ct).ConfigureAwait(true);

        if (outcome.DailyCapReached)
        {
            // Fishing on would earn nothing, and the client greys its own panel out for the same
            // reason. Ending here is kinder than leaving a timer running for no reward.
            await StopAsync(ct).ConfigureAwait(true);
        }
    }

    /// <summary>Hands the catch to the player grain and tells the client.</summary>
    private async Task<FishingCatchOutcome> BankCatchAsync(
        FishSpeciesSnapshot species,
        bool golden,
        CancellationToken ct
    )
    {
        FishingDefinitionsSnapshot definitions = await _grainFactory
            .GetFishingDefinitionsGrain()
            .GetDefinitionsAsync(ct)
            .ConfigureAwait(true);
        FishingPlayerStateSnapshot state = await _grainFactory
            .GetFishingPlayerGrain(PlayerId)
            .GetStateAsync(_catches, ct)
            .ConfigureAwait(true);

        FishingRodLevelSnapshot? rod = RodFor(definitions, state.RodQuality);
        bool frenzy = IsFrenzyRunning(definitions.Settings, DateTime.UtcNow);

        int weight = Random.Shared.Next(
            species.MinWeight,
            Math.Max(species.MinWeight, species.MaxWeight) + 1
        );
        int multiplier = golden ? rod?.GoldenMultiplier ?? 1000 : rod?.CatchMultiplier ?? 1000;

        int xp = Scale(species.XpReward + (golden ? species.GoldenXpBonus : 0), multiplier);

        if (frenzy)
        {
            xp = Scale(xp, definitions.Settings.FrenzyXpMultiplier);
        }

        FishingCatchOutcome outcome = await _grainFactory
            .GetFishingPlayerGrain(PlayerId)
            .ApplyCatchAsync(
                new FishingCatchProposal
                {
                    SpeciesId = species.Id,
                    Weight = weight,
                    Xp = xp,
                    Currency = Scale(species.CurrencyReward, multiplier),
                    Golden = golden,
                },
                ct
            )
            .ConfigureAwait(true);

        _catches++;
        _stockRemaining--;

        await SendAsync(
                new VortexFishingCatchResultMessageComposer
                {
                    RecordId = outcome.RecordId,
                    SpeciesId = species.Id,
                    Weight = weight,
                    XpGained = outcome.XpGranted,
                    CurrencyGained = outcome.CurrencyGranted,
                    Golden = golden,
                    NewLevel = outcome.NewFishingLevel,
                },
                ct
            )
            .ConfigureAwait(true);

        // The player grain owns the state push, so the client's totals come from one place whether
        // they changed because of a catch, a login or an operator's edit.
        await _grainFactory
            .GetFishingPlayerGrain(PlayerId)
            .PushStateAsync(_catches, ct)
            .ConfigureAwait(true);

        return outcome;
    }

    /// <summary>
    /// Hands the minigame to the client and stops the session's own clock until it answers.
    /// </summary>
    /// <remarks>
    /// The attempt is not timed out here. An unplayed attempt simply leaves the session idle until
    /// the player stops or the grain deactivates, which is a valid if unlucky outcome — and a timer
    /// that resumed on its own would race a timeline already in flight.
    /// </remarks>
    private async Task StartHookHavocAsync(
        FishSpeciesSnapshot species,
        FishingSettingsSnapshot settings,
        CancellationToken ct
    )
    {
        _attemptId = _sightingId;
        _attemptSeed = Random.Shared.Next(1, int.MaxValue);
        _attemptSpecies = species;

        await SendAsync(
                new VortexHookHavocStartedMessageComposer
                {
                    AttemptId = _attemptId,
                    Seed = _attemptSeed,
                    DurationMs = settings.HookHavocDurationMs,
                    FillRate = settings.HookHavocFillRate,
                    Tolerance = settings.HookHavocTolerance,
                },
                ct
            )
            .ConfigureAwait(true);
    }

    /// <summary>Arms the next shadow, or ends the session when the stock is gone.</summary>
    private async Task ContinueOrDepleteAsync(CancellationToken ct)
    {
        if (!IsRunning)
        {
            return;
        }

        if (_stockRemaining <= 0)
        {
            await DepleteAsync(ct).ConfigureAwait(true);

            return;
        }

        await ArmNextSightingAsync(ct).ConfigureAwait(true);
    }

    private async Task DepleteAsync(CancellationToken ct)
    {
        int spotItemId = _spotObjectId.Value;
        int catches = _catches;

        await ShowRodAsync(false, ct).ConfigureAwait(true);

        EndSession();

        await SendAsync(
                new VortexFishingSpotDepletedMessageComposer
                {
                    SpotItemId = spotItemId,
                    Catches = catches,
                },
                ct
            )
            .ConfigureAwait(true);
    }

    /// <summary>
    /// Puts Origins' fishing rod in the player's hand for the length of the session, and takes it
    /// away again. Everyone in the room sees it: this is the only outward sign that somebody is
    /// fishing, since the session itself runs unattended and shows nothing else.
    /// </summary>
    /// <remarks>
    /// The rod is an avatar effect rather than a hand item — Origins anchors its eight drawings to
    /// the avatar's own origin, not to the hand. See the client's <c>docs/vortex-original/fishing.md</c>
    /// for how the bundle is built.
    /// <para>
    /// Failure is swallowed on purpose. Whether an avatar is wearing a rod does not decide whether
    /// the session runs, and a player who walked out of the room between the cast and this call must
    /// not have the session refused because of it.
    /// </para>
    /// </remarks>
    private async Task ShowRodAsync(bool holding, CancellationToken ct)
    {
        FishingSettingsSnapshot settings = await _grainFactory
            .GetFishingDefinitionsGrain()
            .GetSettingsAsync(ct)
            .ConfigureAwait(true);

        if (settings.RodEffectId <= 0)
        {
            return;
        }

        try
        {
            await _grainFactory
                .GetRoomGrain(_roomId)
                .SetAvatarEffectAsync(
                    ActionContext.CreateForPlayer(PlayerId, _roomId),
                    holding ? settings.RodEffectId : 0,
                    ct
                )
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Could not {Action} the fishing rod for player {PlayerId} in room {RoomId}",
                holding ? "show" : "clear",
                PlayerId,
                _roomId
            );
        }
    }

    /// <summary>Plays the spot's splash: state 1 on the furni, which falls back to 0 on its own.</summary>
    /// <remarks>
    /// <para>
    /// The spot bundle has always carried two animations — 0 the idle water, 1 the splash — and
    /// nothing had ever asked for the second one, so a catch happened in perfectly still water. The
    /// splash layer declares <c>loopCount 1</c>, so it plays once and the visualization returns to
    /// the idle sequence by itself; there is no timer here and no state to put back.
    /// </para>
    /// <para>
    /// Failures are swallowed for the same reason <see cref="ShowRodAsync"/> swallows its own: the
    /// room may have unloaded between the catch being banked and this running, and a missing ripple
    /// must never cost the player the fish.
    /// </para>
    /// </remarks>
    private async Task SplashAsync(CancellationToken ct)
    {
        try
        {
            await _grainFactory
                .GetRoomFurni(_roomId)
                .SetFloorItemStateAsync(_spotObjectId, SplashState, ct)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Could not splash the fishing spot {SpotId} in room {RoomId}",
                _spotObjectId,
                _roomId
            );
        }
    }

    private void EndSession()
    {
        _timer?.Dispose();
        _timer = null;
        _zone = null;
        _pendingSpecies = null;
        _attemptId = 0;
        _attemptSpecies = null;
        _stockRemaining = 0;
    }

    private void ArmTimer(Func<object?, CancellationToken, Task> step, int delayMs)
    {
        _timer?.Dispose();
        _timer = this.RegisterGrainTimer(
            step,
            this,
            TimeSpan.FromMilliseconds(Math.Max(1, delayMs)),
            Timeout.InfiniteTimeSpan
        );
    }

    /// <summary>
    /// Picks which species swims past, weighted by rarity.
    /// </summary>
    /// <remarks>
    /// Four axes filter the table first — zone, level, hour and weekday, plus the season the guides
    /// name as a fourth. A species outside any of them is not in the pool at all, which is what makes
    /// a nocturnal fish genuinely unavailable by day rather than merely unlikely.
    /// </remarks>
    private FishSpeciesSnapshot? RollSpecies(
        FishingDefinitionsSnapshot definitions,
        int fishingLevel,
        DateTime nowUtc
    )
    {
        int hourBit = 1 << nowUtc.Hour;
        int weekdayBit = 1 << (int)nowUtc.DayOfWeek;
        int seasonBit = SeasonBitFor(nowUtc);

        ImmutableArray<FishSpeciesSnapshot> pool =
        [
            .. definitions.Species.Where(species =>
                species.ZoneId == _zone!.Id
                && species.RequiredLevel <= fishingLevel
                && (species.ActiveHours & hourBit) != 0
                && (species.ActiveWeekdays & weekdayBit) != 0
                && (species.ActiveSeasons & seasonBit) != 0
            ),
        ];

        int total = pool.Sum(species => Math.Max(0, species.RarityWeight));

        if (total <= 0)
        {
            // Either the pool is empty or every weight is zero. Both mean nothing can be picked, and
            // treating them the same avoids a roll against a zero total.
            return null;
        }

        int roll = Random.Shared.Next(total);

        foreach (FishSpeciesSnapshot species in pool)
        {
            roll -= Math.Max(0, species.RarityWeight);

            if (roll < 0)
            {
                return species;
            }
        }

        return pool[^1];
    }

    /// <summary>
    /// Whether the fish was landed.
    /// </summary>
    /// <remarks>
    /// The catch rate is the whole difficulty model — there is no minigame on an ordinary catch, so
    /// nothing the player does changes their odds. A better rod raises them; a long unattended
    /// session lowers them, down to a floor, which is what makes the same hours spread over several
    /// days worth more than one all-night session.
    /// </remarks>
    private static bool RollCatch(
        FishSpeciesSnapshot species,
        FishingRodLevelSnapshot? rod,
        FishingSettingsSnapshot settings,
        int catchesThisSession
    )
    {
        int rate = Scale(species.CatchRate, rod?.CatchMultiplier ?? 1000);

        rate -= settings.SessionDecayPerCatch * catchesThisSession;
        rate = Math.Clamp(rate, Math.Min(settings.SessionDecayFloor, 1000), 1000);

        return Random.Shared.Next(1000) < rate;
    }

    /// <summary>The tier the quality names, or null when the table has none.</summary>
    private static FishingRodLevelSnapshot? RodFor(
        FishingDefinitionsSnapshot definitions,
        int quality
    )
    {
        FishingRodLevelSnapshot? best = null;

        foreach (FishingRodLevelSnapshot tier in definitions.RodTiers)
        {
            if (tier.Quality <= quality && (best is null || tier.Quality > best.Quality))
            {
                best = tier;
            }
        }

        return best;
    }

    /// <summary>
    /// Whether a Fishing Frenzy is running.
    /// </summary>
    /// <remarks>
    /// Derived from the clock rather than scheduled: Origins runs them every four hours on the hour,
    /// so "are we inside one" is arithmetic on the current time and needs no timer, no persisted
    /// window, and nothing to re-arm after a restart.
    /// </remarks>
    private static bool IsFrenzyRunning(FishingSettingsSnapshot settings, DateTime nowUtc)
    {
        if (settings.FrenzyIntervalHours <= 0 || settings.FrenzyDurationMinutes <= 0)
        {
            return false;
        }

        return nowUtc.Hour % settings.FrenzyIntervalHours == 0
            && nowUtc.Minute < settings.FrenzyDurationMinutes;
    }

    /// <summary>
    /// The season bit for a date, on the meteorological quarters.
    /// </summary>
    /// <remarks>
    /// <strong>How Origins encodes a season is unknown</strong> — the guides say only that seasonal
    /// events exist. Four quarters starting in March is the obvious reading and may well be wrong;
    /// it is here rather than in a table because a species already names which seasons it is in, and
    /// only the mapping from a date to a bit is being guessed at.
    /// </remarks>
    private static int SeasonBitFor(DateTime nowUtc) =>
        nowUtc.Month switch
        {
            3 or 4 or 5 => 1 << 0,
            6 or 7 or 8 => 1 << 1,
            9 or 10 or 11 => 1 << 2,
            _ => 1 << 3,
        };

    /// <summary>Applies a thousandths multiplier. 1000 is ×1.00.</summary>
    private static int Scale(int value, int multiplierThousandths) =>
        (int)((long)value * multiplierThousandths / 1000);

    private async Task SendErrorAsync(
        FishingErrorCode code,
        CancellationToken ct,
        int detail = 0
    ) =>
        await SendAsync(
                new VortexFishingErrorMessageComposer { Code = (int)code, Detail = detail },
                ct
            )
            .ConfigureAwait(true);

    private async Task SendAsync(
        Vortex.Primitives.Networking.IComposer composer,
        CancellationToken ct
    )
    {
        try
        {
            await _grainFactory
                .GetPlayerPresenceGrain(PlayerId)
                .SendComposerAsync(composer)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // The player left mid-session. Ending it here is what stops the timer fishing on for
            // somebody who is gone.
            _logger.LogDebug(
                ex,
                "Fishing send failed for player {PlayerId}; ending the session",
                PlayerId
            );

            EndSession();
        }
    }
}
