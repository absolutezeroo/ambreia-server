using Vortex.Primitives.Action;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.World.Layers;

namespace Vortex.Rooms.Grains.Zones.Interactions;

/// <summary>One use of a zone furni: who, which item, its decor entry, the client's param.</summary>
public sealed record ZoneUse(
    RoomGrain Room,
    ActionContext Ctx,
    IRoomPlayer? Player,
    IRoomFloorItem Item,
    ZoneDecorObject Decor,
    int Param
);
