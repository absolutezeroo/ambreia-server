using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Rooms.Enums.Wired;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Object.Logic;
using Vortex.Primitives.Rooms.Wired;
using Vortex.Rooms.Wired;

namespace Vortex.Rooms.Object.Logic.Furniture.Floor.Wired.Selectors;

[RoomObjectLogic("wf_slc_users_byname")]
public class WiredSelectorEntitiesByName(
    IGrainFactory grainFactory,
    IStuffDataFactory stuffDataFactory,
    IRoomFloorItemContext ctx
) : FurnitureWiredSelectorLogic(grainFactory, stuffDataFactory, ctx)
{
    public override int WiredCode => (int)WiredSelectorType.USERS_BY_NAME;

    public override Task<IWiredSelectionSet> SelectAsync(
        IWiredProcessingContext ctx,
        CancellationToken ct
    )
    {
        WiredSelectionSet output = new WiredSelectionSet();
        HashSet<string> names = ParseNames(_wiredData.StringParam);

        foreach (IRoomAvatar avatar in _ctx.Lookup.Avatars)
        {
            if (avatar is not IRoomPlayer roomPlayer || !names.Contains(roomPlayer.Name))
            {
                continue;
            }

            output.SelectedPlayerIds.Add((int)roomPlayer.PlayerId);
        }

        return Task.FromResult((IWiredSelectionSet)output);
    }

    /// <summary>
    /// One name per line of the box's text area. The client sends the lines joined by tabs
    /// (<c>UsersByName.readStringParamFromForm</c> turns every CR, LF and LF-CR into <c>\t</c>), so
    /// a split on anything else matched a list of one name and nothing longer.
    /// </summary>
    public static HashSet<string> ParseNames(string stringParam) =>
        stringParam
            .Split(
                ['\t', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
