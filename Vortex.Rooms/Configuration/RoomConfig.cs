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
    /// boundary — avatars move on <see cref="AvatarTickMs"/>, rollers on <see cref="RollerTickMs"/>,
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
    public int RollerTickMs { get; init; } = 2000;
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

    /// <summary>
    /// Where camera photos are written: the scene as it arrived, and later the rendered PNG.
    /// </summary>
    /// <remarks>
    /// Deployment config, not hotel config — it names a disk, so it belongs in appsettings rather
    /// than in the admin-editable server config the camera's prices and limits live in. It must sit
    /// outside anything served statically: these files are reached through a route that maps a key to
    /// a path, never by a web server walking a directory.
    ///
    /// <para>
    /// <b>This path has to be a mounted volume in any container deployment.</b> A photo is referenced
    /// forever — the furniture a player bought carries its url in its stuff data — and a container is
    /// replaced on every release. Unmounted, the frames stay on the walls and the images behind them
    /// vanish at the next deploy, which reads as "the camera broke" weeks after the change that did
    /// it. The default resolves under the content root, which is <c>/app</c> in the image, so the
    /// mount belongs on <c>/app/data</c>; see the warning beside it in the Dockerfile.
    /// </para>
    /// </remarks>
    public string CameraPhotoRoot { get; init; } = "data/camera-photos";

    /// <summary>
    /// The longest side, in pixels, of the <c>_small</c> copy of a photo.
    /// </summary>
    /// <remarks>
    /// A photo hung on a wall is not fetched at the size it was taken: the client asks for the
    /// <c>_small</c> variant and hands that one image to the furniture at both room scales, so
    /// whatever comes back is drawn at its own size — and a full-resolution photo on a wall is a
    /// photo the size of the room. 110 is what Habbo's own thumbnails measure and the size of the
    /// slot the navigator shows one in. A knob, because the right number is a question about
    /// artwork rather than about code.
    /// </remarks>
    public int CameraSmallPhotoSize { get; init; } = 110;

    /// <summary>
    /// Where the hotel's sprites are extracted to. A camera photo resolves every name in its scene
    /// against this directory.
    /// </summary>
    /// <remarks>
    /// Loose PNG files, one per asset key, extracted once from the asset pack — the same thing the
    /// reference emulator asks of its operator. The server does not read <c>.nitro</c> bundles and
    /// has no business learning to: a photo that is missing a frame is a hole in a picture, and a
    /// server that decodes bundles is a second asset pipeline to keep in step with the client's.
    /// A hotel with nothing here still takes photos; they come out as the room's shape and colours
    /// with no furniture in them, which is a legible way to find out the directory is empty.
    /// </remarks>
    public string CameraSpriteRoot { get; init; } = "data/camera-sprites";
}
