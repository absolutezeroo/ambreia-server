using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Action;
using Vortex.Primitives.Furniture;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Enums.Wired;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Object.Logic;
using Vortex.Primitives.Rooms.Wired;
using Vortex.Rooms.Wired.Rules;

namespace Vortex.Rooms.Object.Logic.Furniture.Floor.Wired.Actions;

/// <summary>
/// Puts the furni back the way they were when the box was saved — Habbo's "match furni to position
/// and state", the box every reset button in every game room is built on.
/// </summary>
/// <remarks>
/// Four boolean int params, in the client's own order (actiontypes/ActionCode3): [0] state,
/// [1] direction, [2] position, [3] altitude. Each says whether that aspect is restored; a box with
/// none of them checked is a box that does nothing, which is the client's own default.
/// <para>
/// The recording is taken when the box is <em>saved</em>, not when it fires — see
/// <see cref="FurnitureWiredLogic.HasStateSnapshot"/>. Arrange the room, save the box, and it
/// returns to that arrangement forever after.
/// </para>
/// <para>
/// Position and altitude travel together through one movement call, so a furni that changes both
/// moves once rather than twice. A destination the room refuses leaves that furni where it is: the
/// alternative is a half-restored board, and unlike the group move there is no arrangement to
/// preserve between the members — each one is independently right or wrong.
/// </para>
/// </remarks>
[RoomObjectLogic("wf_act_match_to_sshot")]
// The hotel's furni is named wf_act_match_to_sshot and binds by wf_act_match_to_sshot_new.
[RoomObjectLogic("wf_act_match_to_sshot_new")]
public class WiredActionMatchToSnapshot(
    IGrainFactory grainFactory,
    IStuffDataFactory stuffDataFactory,
    IRoomFloorItemContext ctx
) : FurnitureWiredActionLogic(grainFactory, stuffDataFactory, ctx)
{
    private const int RestoreState = 0;

    private const int RestoreDirection = 1;

    private const int RestorePosition = 2;

    private const int RestoreAltitude = 3;

    public override int WiredCode => (int)WiredActionType.MATCH_TO_SNAPSHOT;

    public override bool HasStateSnapshot => true;

    public override List<IWiredParamRule> GetIntParamRules() =>
        [
            new WiredRangeParamRule(0, 1, 0), // state
            new WiredRangeParamRule(0, 1, 0), // direction
            new WiredRangeParamRule(0, 1, 0), // position
            new WiredRangeParamRule(0, 1, 0), // altitude
        ];

    public override List<WiredFurniSourceType[]> GetAllowedFurniSources() =>
        [
            [
                WiredFurniSourceType.SelectedItems,
                WiredFurniSourceType.SelectorItems,
                WiredFurniSourceType.SnapshotItems,
                WiredFurniSourceType.TriggeredItem,
            ],
        ];

    public override async Task<bool> ExecuteAsync(IWiredExecutionContext ctx, CancellationToken ct)
    {
        if (_wiredData.Snapshots.Count == 0 || !AnythingToRestore())
        {
            return true;
        }

        foreach (WiredFurniStateSnapshot snapshot in _wiredData.Snapshots)
        {
            if (
                !_ctx.Lookup.TryFindItem(snapshot.FurniId, out IRoomItem? item)
                || item is not IRoomFloorItem floorItem
            )
            {
                continue;
            }

            await RestoreAsync(ctx, item, floorItem, snapshot);
        }

        return true;
    }

    private async Task RestoreAsync(
        IWiredExecutionContext ctx,
        IRoomItem item,
        IRoomFloorItem floorItem,
        WiredFurniStateSnapshot snapshot
    )
    {
        if (Restores(RestoreState) && item.Logic.GetState() != snapshot.State)
        {
            await ctx.ProcessItemStateUpdateAsync(item, snapshot.State);
        }

        bool position = Restores(RestorePosition);
        bool altitude = Restores(RestoreAltitude);
        bool direction = Restores(RestoreDirection);

        if (!position && !altitude && !direction)
        {
            return;
        }

        int x = position ? snapshot.X : floorItem.X;
        int y = position ? snapshot.Y : floorItem.Y;
        Rotation rotation = direction ? (Rotation)snapshot.Rotation : floorItem.Rotation;
        Altitude? height = altitude ? Altitude.FromInt(snapshot.Z) : null;

        if (
            x == floorItem.X
            && y == floorItem.Y
            && rotation == floorItem.Rotation
            && (!altitude || snapshot.Z == floorItem.Z.ToInt())
        )
        {
            return;
        }

        if (
            !await _ctx.Furni.ValidateFloorItemPlacementAsync(
                ActionContext.Wired,
                floorItem.ObjectId,
                x,
                y,
                rotation
            )
        )
        {
            return;
        }

        await ctx.ProcessFloorItemMovementAsync(floorItem, _ctx.Map.ToIdx(x, y), height, rotation);
    }

    private bool Restores(int index) =>
        _wiredData.IntParams.Count > index && _wiredData.GetIntParam<int>(index) != 0;

    private bool AnythingToRestore() =>
        Restores(RestoreState)
        || Restores(RestoreDirection)
        || Restores(RestorePosition)
        || Restores(RestoreAltitude);
}
