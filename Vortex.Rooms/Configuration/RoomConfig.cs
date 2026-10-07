using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;

namespace Vortex.Rooms.Configuration;

public class RoomConfig
{
    public const string SECTION_NAME = "Vortex:Rooms";

    public Altitude MaxStackHeight { get; init; } = Altitude.FromInt(4000);
    public RoomScaleType DefaultRoomScale { get; init; } = RoomScaleType.Normal;
    public Altitude MaxStepHeight { get; init; } = Altitude.FromInt(200);
    public bool PlaceItemsOnAvatars { get; init; } = true;
    public bool EnableDiagonalChecking { get; init; } = true;

    public int RoomCheckMs { get; init; } = 300000;
    public int RoomDeactivationDelayMs { get; init; } = 1800000;

    /// <summary>
    /// How often live room occupancy is written back to <c>rooms.users_now</c>, and the top-rated
    /// ranking recomputed with it.
    /// <para>
    /// It cannot ride on <see cref="RoomCheckMs"/>: that sweep is the deactivation clock and runs
    /// every five minutes, which is far too stale for the navigator, whose room lists are ordered
    /// and <em>truncated</em> in SQL on this column — a busy room the database still believes is
    /// empty does not merely sort low, it falls out of the page entirely.
    /// </para>
    /// </summary>
    public int RoomPopulationFlushMs { get; init; } = 30000;

    /// <summary>How many rooms the top-rated ranking covers. The client hides the field unless the
    /// value is positive, so everything past this stays honestly unranked.</summary>
    public int RoomRankingSize { get; init; } = 100;

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
    /// boundary — avatars move on <see cref="AvatarTickMs"/>, pets on their own tick,
    /// items flush on <see cref="DirtyItemsTickMs"/> — and this only decides how promptly a boundary
    /// that has come due gets noticed, so the error is bounded by one tick.
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
    public int DirtyItemsTickMs { get; init; } = 2000;
    public int MaxDirtyItemsPerFlush { get; init; } = 100;
    public int MaxTileHeightsPerFlush { get; init; } = 200;
    public int MaxPathNodes { get; init; } = 4096;

    /// <summary>What a room's name becomes when a moderator wipes an offensive one from the room
    /// tool. Kept configurable so a hotel can localise it; the description is blanked outright.</summary>
    public string ModeratedRoomNamePlaceholder { get; init; } = "Inappropriate room name";

    public int MaxVisitorsLimit { get; init; } = 50;

    public int DoorbellTimeoutMs { get; init; } = 20000;

    /// <summary>Business cap on how many items one player may offer on their side of a trade. The
    /// parser also bounds a single batch (<c>ProtocolLimitsConfig.MaxTradeItems</c>) for wire safety;
    /// this is the per-side offer ceiling enforced by the trade session.</summary>
    public int MaxTradeItemsPerSide { get; init; } = 1500;

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
