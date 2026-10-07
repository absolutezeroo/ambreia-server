using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Action;
using Vortex.Primitives.Furniture.StuffData;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object.Furniture;

namespace Vortex.Primitives.Rooms.Object.Logic.Furniture;

public interface IFurnitureLogic<out TObject, out TLogic, out TContext>
    : IRoomObjectLogic<TObject, TLogic, TContext>,
        IFurnitureLogic
    where TObject : IRoomItem<TObject, TLogic, TContext>
    where TContext : IRoomItemContext<TObject, TLogic, TContext>
    where TLogic : IFurnitureLogic<TObject, TLogic, TContext>
{
    new TContext Context { get; }
}

public interface IFurnitureLogic : IRoomObjectLogic
{
    new IRoomItemContext Context { get; }
    public IStuffData StuffData { get; }

    /// <summary>
    /// Persists whatever the caller just wrote into <see cref="StuffData"/> and pushes the refresh
    /// to the room — the same two steps <see cref="SetStateAsync"/> performs, split out for the
    /// furniture whose data is not a state at all: a mannequin's outfit, a sticky note's text.
    /// Mutating <see cref="StuffData"/> without calling this leaves the change in memory only, so
    /// it survives until the grain deactivates and then silently disappears.
    /// </summary>
    public Task PersistStuffDataAsync(bool refresh = true);

    public FurnitureUsageType GetUsagePolicy();
    public bool CanToggle();
    public Altitude GetStackHeight();
    public int GetState();
    public string GetLegacyString();
    public int GetNextToggleableState();
    public int GetPrevToggleableState();
    public int GetRandomToggleableState();
    public Task SetStateAsync(int state, bool refresh = true);
    public Task OnStateChangedAsync(CancellationToken ct);
    public Task OnMoveAsync(ActionContext ctx, int prevIdx, CancellationToken ct);
    public Task OnPlaceAsync(ActionContext ctx, CancellationToken ct);
    public Task OnPickupAsync(ActionContext ctx, CancellationToken ct);
    public Task OnUseAsync(ActionContext ctx, int param, CancellationToken ct);
    public Task OnClickAsync(ActionContext ctx, int param, CancellationToken ct);
}
