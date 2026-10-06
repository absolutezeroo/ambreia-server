using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Object.Logic;

namespace Vortex.Rooms.Object.Logic.Furniture.Floor;

// "nest" is what the shipped catalogue carries -- Arcturus' interaction_type. "pet_nest" is Vortex's own name and matches no
// definition in the dump, which is why every nest resolved to default_floor. The assets are no help
// here: they call a nest furniture_multistate, the same trap the gate falls into.
[RoomObjectLogic("nest")]
[RoomObjectLogic("pet_nest")]
public class FurniturePetNestLogic(IStuffDataFactory stuffDataFactory, IRoomFloorItemContext ctx)
    : FurnitureFloorLogic(stuffDataFactory, ctx) { }
