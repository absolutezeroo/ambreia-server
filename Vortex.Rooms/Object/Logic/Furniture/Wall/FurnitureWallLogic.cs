using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Rooms.Object.Furniture.Wall;
using Vortex.Primitives.Rooms.Object.Logic;
using Vortex.Primitives.Rooms.Object.Logic.Furniture;

namespace Vortex.Rooms.Object.Logic.Furniture.Wall;

/// <summary>
/// The plain wall furni.
/// </summary>
/// <remarks>
/// Shares <c>furniture_basic</c> and <c>furniture_multistate</c> with
/// <see cref="Floor.FurnitureFloorLogic"/> — the client uses one name across both families, and 824
/// wall definitions carry these two. That sharing is why registrations are keyed by family: before,
/// a single name could only point at one of the two classes, and a wall item resolving to the floor
/// logic could not be constructed at all.
/// </remarks>
[RoomObjectLogic("default_wall")]
[RoomObjectLogic("furniture_basic")]
[RoomObjectLogic("furniture_multistate")]
[RoomObjectLogic("furniture_muItistate")]
[RoomObjectLogic("furniture_static")]
// A window is plain wall furni on the server; its plane mask is purely client-side
// (FurnitureWindowLogic.as). Unregistered, every placement logged a fallback warning.
[RoomObjectLogic("furniture_window")]
public class FurnitureWallLogic(IStuffDataFactory stuffDataFactory, IRoomWallItemContext ctx)
    : FurnitureLogic<IRoomWallItem, IFurnitureWallLogic, IRoomWallItemContext>(
        stuffDataFactory,
        ctx
    ),
        IFurnitureWallLogic
{
    IRoomWallItemContext IFurnitureWallLogic.Context => Context;
}
