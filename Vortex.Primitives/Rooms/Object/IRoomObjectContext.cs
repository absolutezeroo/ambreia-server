using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Events;
using Vortex.Primitives.Rooms.Games;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.Rooms.Object.Logic;

namespace Vortex.Primitives.Rooms.Object;

public interface IRoomObjectContext<out TObject, out TLogic, out TSelf> : IRoomObjectContext
    where TObject : IRoomObject<TObject, TLogic, TSelf>
    where TSelf : IRoomObjectContext<TObject, TLogic, TSelf>
    where TLogic : IRoomObjectLogic<TObject, TLogic, TSelf>
{
    new TObject RoomObject { get; }
}

public interface IRoomObjectContext
{
    public RoomId RoomId { get; }

    /// <summary>The room this object lives in, as its core facet. Room-object logic overwhelmingly
    /// only needs this much; anything wider goes through <see cref="RoomAs{TFacet}"/> so the extra
    /// reach is visible at the call site.</summary>
    public IRoomCore Room { get; }

    /// <summary>
    /// The same room, viewed through another facet -- for the handful of furniture whose behaviour
    /// is owned by a specific room subsystem (crackables, mystery boxes, monsterplant seeds).
    /// </summary>
    /// <remarks>
    /// This is the very activation the caller is already running inside, not a new grain, so the
    /// call it returns stays an ordinary in-process call. It deliberately does not hand back an
    /// Orleans grain reference: <c>RoomGrain</c> is not <c>[Reentrant]</c>, so a reference-routed
    /// call from inside the room's own turn would queue behind that turn and deadlock.
    /// </remarks>
    public TFacet RoomAs<TFacet>()
        where TFacet : IAddressable;

    /// <summary>Finding the other objects in the room.</summary>
    public IRoomLookup Lookup { get; }

    /// <summary>Tile arithmetic, walkability and tile contents.</summary>
    public IRoomMapAccess Map { get; }

    /// <summary>The room's game runtime: teams, scores, the match lifecycle and the one seam arena
    /// furniture reports through. There is no per-game property here and there must never be one —
    /// a component says what happened to it and the runtime routes it.</summary>
    public IRoomGameAccess Game { get; }

    /// <summary>The room clock, in milliseconds. Monotonic within an activation.</summary>
    public long NowMs();

    /// <summary>The room clock reading the current session started from.</summary>
    public long EpochMs { get; }

    /// <summary>The room's owner.</summary>
    public PlayerId OwnerId { get; }

    public RoomObjectId ObjectId { get; }
    public IRoomObject RoomObject { get; }

    public Task PublishRoomEventAsync(RoomEvent evt, CancellationToken ct);
    public Task SendComposerToRoomAsync(IComposer composer);
}
