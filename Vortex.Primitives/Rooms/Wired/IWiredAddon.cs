using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Snapshots.Wired;

namespace Vortex.Primitives.Rooms.Wired;

public interface IWiredAddon : IWiredBox
{
    public Task<bool> MutatePolicyAsync(IWiredProcessingContext ctx, CancellationToken ct);
    public Task BeforeEffectsAsync(IWiredProcessingContext ctx, CancellationToken ct);
    public Task AfterEffectsAsync(IWiredProcessingContext ctx, CancellationToken ct);

    /// <summary>
    /// The add-on's chance to rewrite a text an action is about to say, for the placeholder boxes.
    /// Returns the text untouched by default, so an add-on that is not about text costs a call and
    /// nothing else.
    /// </summary>
    public Task<string> ApplyToTextAsync(
        string text,
        IWiredExecutionContext ctx,
        CancellationToken ct
    );

    /// <summary>
    /// The add-on's chance to turn a furni an action is about to move to (<paramref name="targetX"/>,
    /// <paramref name="targetY"/>). Null keeps the rotation the action asked for.
    /// </summary>
    /// <remarks>
    /// This and <see cref="ShapeFloorItemMoveAsync"/> run inside the move itself, not in
    /// <see cref="AfterEffectsAsync"/>: every action gets its own execution context, flushed to the
    /// room the moment the action returns, so by the end of a pile's effects its moves are long sent.
    /// </remarks>
    public Task<Rotation?> RotateFloorItemMoveAsync(
        IRoomFloorItem item,
        int targetX,
        int targetY,
        IWiredExecutionContext ctx,
        CancellationToken ct
    );

    /// <summary>
    /// The add-on's chance to change how a furni move that has already happened is animated —
    /// duration, overshoot, arc — before it is sent to the room.
    /// </summary>
    public Task<WiredFloorItemMovementSnapshot> ShapeFloorItemMoveAsync(
        IRoomFloorItem item,
        WiredFloorItemMovementSnapshot move,
        IWiredExecutionContext ctx,
        CancellationToken ct
    );
}
