using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Events;
using Vortex.Primitives.Rooms.Games;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Logic;
using Vortex.Rooms.Grains;

namespace Vortex.Rooms.Object;

public abstract class RoomObjectContext<TObject, TLogic, TSelf>(
    RoomGrain roomGrain,
    TObject roomObject
) : IRoomObjectContext<TObject, TLogic, TSelf>
    where TObject : IRoomObject<TObject, TLogic, TSelf>
    where TSelf : IRoomObjectContext<TObject, TLogic, TSelf>
    where TLogic : IRoomObjectLogic<TObject, TLogic, TSelf>
{
    protected readonly RoomGrain _roomGrain = roomGrain;
    protected readonly TObject _roomObject = roomObject;

    public RoomId RoomId => _roomGrain._state.RoomId;
    public IRoomCore Room => _roomGrain;

    // RoomGrain implements every facet, and this is the live activation the caller already runs in,
    // so the cast is free and the resulting call is a direct in-process one. See the remarks on
    // IRoomObjectContext.RoomAs for why this is not AsReference.
    public TFacet RoomAs<TFacet>()
        where TFacet : IAddressable => (TFacet)(object)_roomGrain;

    // The room's in-process capabilities. RoomGrain implements each explicitly, so these are the
    // same activation seen through narrow, non-grain contracts. There is ONE game contract however
    // many games the room hosts: a per-game property here is what made adding a game an edit to the
    // core, and there must never be another one.
    public IRoomLookup Lookup => _roomGrain;
    public IRoomMapAccess Map => _roomGrain;
    public IRoomGameAccess Game => _roomGrain;

    public long NowMs() => _roomGrain.NowMs();

    public long EpochMs => _roomGrain._state.EpochMs;

    public PlayerId OwnerId => _roomGrain._state.RoomSnapshot.OwnerId;

    public RoomObjectId ObjectId => _roomObject.ObjectId;
    public TObject RoomObject => _roomObject;

    IRoomObject IRoomObjectContext.RoomObject => RoomObject;

    public Task PublishRoomEventAsync(RoomEvent evt, CancellationToken ct) =>
        _roomGrain.PublishRoomEventAsync(evt, ct);

    public Task SendComposerToRoomAsync(IComposer composer) =>
        _roomGrain.SendComposerToRoomAsync(composer);
}
