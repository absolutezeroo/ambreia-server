using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;

namespace Vortex.Primitives.Rooms.Object.Logic.Furniture;

public interface IFurnitureFloorLogic<out TObject, out TLogic, out TContext>
    : IFurnitureLogic<TObject, TLogic, TContext>
    where TObject : IRoomFloorItem<TObject, TLogic, TContext>
    where TContext : IRoomFloorItemContext<TObject, TLogic, TContext>
    where TLogic : IFurnitureFloorLogic<TObject, TLogic, TContext>
{
    new TContext Context { get; }
}

public interface IFurnitureFloorLogic
    : IFurnitureLogic<IRoomFloorItem, IFurnitureFloorLogic, IRoomFloorItemContext>
{
    new IRoomFloorItemContext Context { get; }
    public bool CanStack();
    public bool CanWalk();
    public bool CanSit();
    public bool CanLay();
    public Altitude GetPostureOffset();

    /// <summary>
    /// The floor item's <c>extra</c> field on the wire. Zero for almost everything; the magic tiles
    /// use it to carry their multi-walk flag, which the client reads back as
    /// <c>furniture_extra</c>.
    /// </summary>
    public int GetExtra();
    public Task OnInvokeAsync(IRoomAvatarContext ctx, CancellationToken ct);
    public Task OnWalkOnAsync(IRoomAvatarContext ctx, CancellationToken ct);
    public Task OnWalkOffAsync(IRoomAvatarContext ctx, CancellationToken ct);
}
