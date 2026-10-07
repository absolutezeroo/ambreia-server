using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Action;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Rooms.Events.Avatar;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Object.Logic;
using Vortex.Primitives.Rooms.Object.Logic.Furniture;

namespace Vortex.Rooms.Object.Logic.Furniture.Floor;

/// <summary>
/// The plain floor furni: stackable, optionally sittable, and toggling through its states on use.
/// </summary>
/// <remarks>
/// The client names carried here are what the assets declare for the overwhelming majority of the
/// catalogue — <c>furniture_multistate</c> alone covers ~34 000 definitions, <c>furniture_basic</c>
/// another ~5 800. Their behaviour is already exactly this class (<see cref="OnUseAsync"/> advances
/// the state, walkability comes from the definition), so they were resolving correctly by accident,
/// through the provider's fallback. Naming them makes that explicit and, more usefully, keeps the
/// "logic not found" warning meaningful: it now only fires for behaviour Vortex genuinely lacks.
/// <para>
/// <c>furniture_muItistate</c> is not a typo here — it is a typo in the shipped assets, with a
/// capital i in place of the l, on seven definitions. The client's own factory does not match it
/// either, so those furni fall back on both sides; accepting it server-side costs nothing and
/// records why the odd spelling exists.
/// </para>
/// <para>
/// <c>furniture_purchasable_clothing</c> — 2 862 definitions, the largest single name still falling
/// back — belongs here rather than in a class of its own, and the client says why: its logic extends
/// <c>FurnitureMultiStateLogic</c> and overrides <c>useObject()</c> to raise a confirmation dialog
/// instead of sending anything. No use ever reaches the server for one of these, and the
/// redemption packet it would send has no handler since the cut of 2026-10-07. So the server-side
/// behaviour is exactly the plain floor furni's, and the only thing the fallback cost was a
/// warning per placement.
/// </para>
/// </remarks>
[RoomObjectLogic("default_floor")]
[RoomObjectLogic("furniture_basic")]
[RoomObjectLogic("furniture_multistate")]
[RoomObjectLogic("furniture_muItistate")]
[RoomObjectLogic("furniture_static")]
[RoomObjectLogic("furniture_purchasable_clothing")]
public class FurnitureFloorLogic(IStuffDataFactory stuffDataFactory, IRoomFloorItemContext ctx)
    : FurnitureLogic<IRoomFloorItem, IFurnitureFloorLogic, IRoomFloorItemContext>(
        stuffDataFactory,
        ctx
    ),
        IFurnitureFloorLogic
{
    IRoomFloorItemContext IFurnitureFloorLogic.Context => Context;

    public virtual bool CanStack() => _ctx.Definition.CanStack;

    public virtual bool CanWalk() => _ctx.Definition.CanWalk;

    public virtual bool CanSit() => _ctx.Definition.CanSit;

    public virtual bool CanLay() => _ctx.Definition.CanLay;

    public virtual Altitude GetPostureOffset()
    {
        if (CanSit())
        {
            return GetStackHeight();
        }

        if (CanLay())
        {
            return GetStackHeight();
        }

        return Altitude.Zero;
    }

    public override Altitude GetStackHeight() => _ctx.Definition.StackHeight;

    public virtual int GetExtra() => 0;

    public override Task OnStateChangedAsync(CancellationToken ct)
    {
        _ctx.RefreshTile();

        return base.OnStateChangedAsync(ct);
    }

    public virtual Task OnInvokeAsync(IRoomAvatarContext ctx, CancellationToken ct) =>
        Task.CompletedTask;

    public virtual Task OnWalkOnAsync(IRoomAvatarContext ctx, CancellationToken ct) =>
        _ctx.PublishRoomEventAsync(
            new AvatarWalkOnFurniEvent
            {
                RoomId = _ctx.RoomId,
                CausedBy = ActionContext.CreateForObjectContext(ctx),
                ObjectId = ctx.ObjectId,
                FurniId = _ctx.ObjectId,
            },
            ct
        );

    public virtual Task OnWalkOffAsync(IRoomAvatarContext ctx, CancellationToken ct) =>
        _ctx.PublishRoomEventAsync(
            new AvatarWalkOffFurniEvent
            {
                RoomId = _ctx.RoomId,
                CausedBy = ActionContext.CreateForObjectContext(ctx),
                ObjectId = ctx.ObjectId,
                FurniId = _ctx.ObjectId,
            },
            ct
        );
}
