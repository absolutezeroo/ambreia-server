using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Action;
using Vortex.Primitives.Inventory.Snapshots;

namespace Vortex.Primitives.Rooms.Grains;

public partial interface IRoomFurni
{
    /// <summary>
    /// Applies a wallpaper, floor or landscape from the actor's inventory to this room: stores the
    /// pattern code, deletes the item and broadcasts the new room property. False when the actor
    /// may not place furni here, the item is not theirs or not a decoration, or carries no code.
    /// Nothing calls it since the inventory left with the cut of 2026-10-07.
    /// </summary>
    public Task<bool> ApplyDecorationAsync(
        ActionContext ctx,
        FurnitureItemSnapshot item,
        CancellationToken ct
    );
}
