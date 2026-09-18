using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;

namespace Vortex.Rooms.Configuration;

public class RoomConfig : IWiredLimits
{
    public const string SECTION_NAME = "Vortex:Rooms";

    public Altitude MaxStackHeight { get; init; } = Altitude.FromInt(4000);
    public RoomScaleType DefaultRoomScale { get; init; } = RoomScaleType.Normal;
    public int DefaultWallHeight { get; init; } = 0;
    public Altitude MaxStepHeight { get; init; } = Altitude.FromInt(200);
    public bool PlaceItemsOnAvatars { get; init; } = true;
    public bool EnableDiagonalChecking { get; init; } = true;

    public int RoomCheckMs { get; init; } = 300000;
    public int RoomDeactivationDelayMs { get; init; } = 1800000;

    /// <summary>
    /// How often the room wakes up to look at its clocks.
    /// <para>
    /// <b>50 does not mean twenty times a second on Windows.</b> The default system timer quantum is
    /// 15.625 ms, so a 50 ms period is served at the next multiple — 62.5 ms, or sixteen times a
    /// second. A benchmark run measured 63.2 ms and that is the reason; it is the operating system's
    /// clock, not contention and not a fault in the tick. Anything below 16 Hz under load <em>is</em>
    /// contention: a run during a login storm managed 11.
    /// </para>
    /// <para>
    /// It costs nothing in pacing. Every system that cares about timing keeps its own wall-clock
    /// boundary — avatars move on <see cref="AvatarTickMs"/>, rollers on <see cref="RollerTickMs"/>,
    /// items flush on <see cref="DirtyItemsTickMs"/> — and this only decides how promptly a boundary
    /// that has come due gets noticed, so the error is bounded by one tick. The single exception is
    /// <see cref="WiredTickMs"/>, which is as fine as this one and is therefore capped by it.
    /// </para>
    /// </summary>
    public int RoomTickMs { get; init; } = 50;
    public int AvatarTickMs { get; init; } = 500;

    /// <summary>
    /// How long a hand item stays in the hand. Habbo shows one for about half a minute and then
    /// takes it away; it is a display timer rather than a rule about the item, which is why it sits
    /// with the room's other clocks.
    /// </summary>
    public int HandItemDurationMs { get; init; } = 30_000;
    public PetConfig Pet { get; init; } = new();
    public int RollerTickMs { get; init; } = 2000;
    public int WiredTickMs { get; init; } = 50;
    public int DirtyItemsTickMs { get; init; } = 2000;
    public int MaxDirtyItemsPerFlush { get; init; } = 100;
    public int MaxTileHeightsPerFlush { get; init; } = 200;
    public int MaxPathNodes { get; init; } = 4096;

    /// <summary>What a room's name becomes when a moderator wipes an offensive one from the room
    /// tool. Kept configurable so a hotel can localise it; the description is blanked outright.</summary>
    public string ModeratedRoomNamePlaceholder { get; init; } = "Inappropriate room name";

    /// <summary>
    /// How deep one "execute stacks" chain may go before the wired engine stops entering piles.
    /// <para>
    /// The default is 8 because 8 is what rooms have actually been enforcing: this property read 20
    /// and nothing read this property — <c>RoomWiredSystem</c> enforced a private const of 8
    /// (RFW-101). Wiring the knob up without lowering the default would have been a silent
    /// behaviour change dressed as a refactor. Habbo's own limit is <c>UNKNOWN</c>; raise this
    /// deliberately, watching <c>Vortex.wired.chain.stopped{reason=depth}</c>.
    /// </para>
    /// </summary>
    public int WiredMaxDepth { get; init; } = 8;
    public int WiredMaxScheduledPerTick { get; init; } = 64;
    public int WiredMaxEventsPerTick { get; init; } = 64;
    public int WiredSelectorMaxAreaSize { get; init; } = 100;
    public int WiredSelectedItemsLimit { get; init; } = 20;

    /// <summary>
    /// How many players one wired action may act on in a single execution. The furni side of a
    /// selection has been bounded since the beginning — <see cref="WiredSelectorMaxAreaSize"/> and
    /// <see cref="WiredSelectedItemsLimit"/> — and the player side never was, so "give furni to
    /// everyone" ran one write, one full inventory reload and one client push per selected player,
    /// sequentially, inside the room's turn. That is the only fan-out in the engine a player builds
    /// rather than an operator configures.
    /// <para>
    /// Twice <see cref="MaxVisitorsLimit"/>, so no room a hotel actually runs reaches it and it only
    /// bites the room built to hurt. Trims are counted as <c>selection-limit</c> on
    /// <c>Vortex.wired.chain.stopped</c>.
    /// </para>
    /// </summary>
    public int WiredSelectedPlayersLimit { get; init; } = 100;
    public bool WiredAllowWallFurni { get; init; } = true;
    public int WiredMaxIntParams { get; init; } = 16;
    public int WiredNeighborhoodRadius { get; init; } = 5;

    /// <summary>Hard cap on the wired event queue. WiredMaxEventsPerTick bounds the tick's work;
    /// this bounds memory under a sustained event storm — events past the cap are dropped and
    /// reported once per tick in the room's wired log.</summary>
    public int WiredMaxQueuedEvents { get; init; } = 512;

    /// <summary>Hard cap on the pending scheduled executions a room may hold. The same bound as
    /// <see cref="WiredMaxQueuedEvents"/>, one layer further on: WiredMaxScheduledPerTick bounds
    /// what a tick <em>runs</em>, and bounded no better than the event queue does what accumulates
    /// when a room schedules faster than it drains. A pending execution is much heavier than an
    /// event — it holds the pile, its actions, the selection and the processing context — so this
    /// was the cheapest room in the hotel to exhaust memory from.</summary>
    public int WiredMaxPendingExecutions { get; init; } = 512;

    /// <summary>How long a wired box stays lit after firing before the wired system reverts its
    /// visual state.</summary>
    public int WiredFlashDurationMs { get; init; } = 500;

    public int MaxVisitorsLimit { get; init; } = 50;

    public int DoorbellTimeoutMs { get; init; } = 20000;

    /// <summary>Business cap on how many items one player may offer on their side of a trade. The
    /// parser also bounds a single batch (<c>ProtocolLimitsConfig.MaxTradeItems</c>) for wire safety;
    /// this is the per-side offer ceiling enforced by the trade session.</summary>
    public int MaxTradeItemsPerSide { get; init; } = 1500;

    /// <summary>
    /// How many items one wired chest may hold. There was no ceiling at all, and every chest
    /// operation loads the whole content into the room's turn — listing it, settling a contract
    /// against it, paying out of it — so the cost of touching a chest was whatever a player had
    /// chosen to put in it.
    /// <para>
    /// Also the ceiling on one deposit screen, which is the same bound arriving earlier: the staged
    /// list is held in the room's memory and turns into an <c>IN</c> clause of its own size.
    /// </para>
    /// </summary>
    public int WiredChestCapacity { get; init; } = 1000;

    /// <summary>
    /// How many song disks one jukebox holds. The client is told this number and draws that many
    /// slots, so it is a display bound as much as a limit; the refusal it triggers has its own
    /// message (<c>JukeboxPlayListFull</c>) and its own dialog.
    /// </summary>
    public int JukeboxCapacity { get; init; } = 20;

    /// <summary>How long a half-open mystery box keeps waiting for its other half. The client's wait
    /// dialog has no timer of its own, so without this a player who walked away leaves the box
    /// permanently reserved against everyone else.</summary>
    public int MysteryBoxWaitTimeoutMs { get; init; } = 120000;

    /// <summary>Cap on the inscription a mystery trophy can carry, so an oversized string cannot be
    /// pushed into the trophy's stuff data (which every viewer of the room then receives).</summary>
    public int MysteryTrophyInscriptionMaxLength { get; init; } = 100;

    /// <summary>Minimum seconds between chat lines from the same player under each room-configured
    /// <c>ChatFloodSensitivityType</c>, keyed Extra/Normal/Minimal (SEC-03). A line arriving before
    /// its slot sends <c>FloodControlMessageComposer</c> with the remaining wait instead of being
    /// spoken. These are starting defaults, not a tuned game-balance decision - adjust freely.</summary>
    public int[] ChatFloodIntervalSeconds { get; init; } = [4, 2, 1];

    /// <summary>
    /// How many lines a player may send faster than <see cref="ChatFloodIntervalSeconds"/> before
    /// the room starts refusing them. Habbo lets a burst through and only then gates: a limit of
    /// one — which is what no allowance at all amounts to — blocks the second line of any sentence
    /// somebody types quickly, and reads as the chat being broken rather than protected.
    /// </summary>
    public int ChatFloodAllowance { get; init; } = 5;
}
