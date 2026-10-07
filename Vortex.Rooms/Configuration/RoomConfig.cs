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

    public int DirtyItemsTickMs { get; init; } = 2000;
    public int MaxDirtyItemsPerFlush { get; init; } = 100;
    public int MaxTileHeightsPerFlush { get; init; } = 200;
    public int MaxPathNodes { get; init; } = 4096;

    public int MaxVisitorsLimit { get; init; } = 50;
}
