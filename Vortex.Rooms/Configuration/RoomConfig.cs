using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;

namespace Vortex.Rooms.Configuration;

public class RoomConfig
{
    public const string SECTION_NAME = "Vortex:Rooms";

    public Altitude MaxStackHeight { get; init; } = Altitude.FromInt(4000);

    public RoomScaleType DefaultRoomScale { get; init; } = RoomScaleType.Normal;

    /// <summary>How high an avatar climbs in one step.</summary>
    public Altitude MaxStepHeight { get; init; } = Altitude.FromInt(100);

    /// <summary>How far an avatar drops in one step; deliberately more than it can climb.</summary>
    public Altitude MaxDropHeight { get; init; } = Altitude.FromInt(200);

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
    /// boundary — avatars move on <see cref="AvatarTickMs"/>, items flush on <see cref="DirtyItemsTickMs"/> — and this only decides how promptly a boundary
    /// that has come due gets noticed, so the error is bounded by one tick.
    /// </para>
    /// </summary>
    public int RoomTickMs { get; init; } = 50;

    public int AvatarTickMs { get; init; } = 500;

    public int DirtyItemsTickMs { get; init; } = 2000;

    public int MaxDirtyItemsPerFlush { get; init; } = 100;

    public int MaxTileHeightsPerFlush { get; init; } = 200;

    public int MaxPathNodes { get; init; } = 4096;

    /// <summary>How long a zone keeps the entry a passage or a teleport announced for a player.</summary>
    public int ExpectedArrivalTimeoutMs { get; init; } = 30000;

    /// <summary>How deep zone triggers may set each other off before the chain is cut (plan D15).</summary>
    public int ZoneTriggerMaxDepth { get; init; } = 8;

    /// <summary>How many trigger runs one event may set off in all, across every depth (plan D15).</summary>
    public int ZoneTriggerChainBudget { get; init; } = 64;

    /// <summary>How long the editor's test mode waits for the tester's client to sign in (plan D18).</summary>
    public int ZoneTestEntryTimeoutMs { get; init; } = 120000;

    /// <summary>Copies one dungeon can have at once (plan D23); at most ZoneInstanceIds.SlotsPerZone - 1.</summary>
    public int MaxInstancesPerZone { get; init; } = 200;

    /// <summary>The RP actions an avatar may do (an array: configuration replaces it whole). A new
    /// one is a line here and its animation in the client.</summary>
    public string[] EmoteKeys { get; init; } =
    ["wave", "laugh", "clap", "dance", "sad", "surprised"];

    /// <summary>The least time between two RP actions of one player.</summary>
    public int EmoteIntervalMs { get; init; } = 2000;
}
