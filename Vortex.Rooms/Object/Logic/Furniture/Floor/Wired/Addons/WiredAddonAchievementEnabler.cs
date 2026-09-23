using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Action;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Rooms.Enums.Wired;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Object.Logic;
using Vortex.Primitives.Rooms.Snapshots.Wired;
using Vortex.Primitives.Rooms.Wired;
using Vortex.Protocol.Messages.Outgoing.Userdefinedroomevents;

namespace Vortex.Rooms.Object.Logic.Furniture.Floor.Wired.Addons;

/// <summary>
/// "WIRED Add-on: Achievement Enabler" (addon code 2001) — names the wired achievements the room's
/// "progress achievement" effects may advance, one per line, without their <c>ACH_WF_</c>.
/// </summary>
/// <remarks>
/// <para>
/// It does nothing to the pile it stands in. What it enables is room-wide: the room answers
/// <see cref="IRoomWired.GetEnabledAchievementsAsync"/> from every enabler it holds, and that list
/// is both the dropdown the client builds the effect's dialog from (<c>WiredEnvironment</c>) and the
/// only names the effect will advance.
/// </para>
/// <para>
/// The client's own error says "Staff needs to configure the enabled achievements", and it is the
/// furni that makes that true here: nothing sells this box, so whoever has one was given it. There
/// is no separate capability check on saving it.
/// </para>
/// <para>
/// The logic key is derived: no furnidata in any tree has this add-on. A definition that should
/// use it needs <c>logic</c> set to it.
/// </para>
/// </remarks>
[RoomObjectLogic("wf_xtra_achievement_enabler")]
public class WiredAddonAchievementEnabler(
    IGrainFactory grainFactory,
    IStuffDataFactory stuffDataFactory,
    IRoomFloorItemContext ctx
) : FurnitureWiredAddonLogic(grainFactory, stuffDataFactory, ctx)
{
    public override int WiredCode => (int)WiredAddonType.ACHIEVEMENT_ENABLER;

    /// <summary>The names this box enables, trimmed, deduplicated, and stripped of the prefix a
    /// builder writes out of habit.</summary>
    public async Task<IReadOnlyList<string>> GetEnabledAchievementsAsync(CancellationToken ct)
    {
        await LoadWiredAsync(ct);

        return Parse(_wiredData.StringParam);
    }

    internal static IReadOnlyList<string> Parse(string text)
    {
        List<string> names = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (string line in text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            string name = line.Trim();

            foreach (string prefix in (string[])["ACH_WF_", "WF_"])
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    name = name[prefix.Length..];

                    break;
                }
            }

            if (name.Length > 0 && seen.Add(name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    /// <summary>
    /// Saves, then tells the room what is now enabled: the effect's dropdown is built from the last
    /// <c>WiredEnvironment</c> the client received, so without this a builder would have to leave and
    /// come back before the achievement they just enabled could be picked.
    /// </summary>
    public override async Task<bool> ApplyWiredUpdateAsync(
        ActionContext ctx,
        WiredUpdateRequest update,
        CancellationToken ct
    )
    {
        if (!await base.ApplyWiredUpdateAsync(ctx, update, ct))
        {
            return false;
        }

        IRoomWired room = _ctx.RoomAs<IRoomWired>();
        WiredClickUserSnapshot clickUser = await room.GetClickUserStateAsync(ct);

        await _ctx.Room.SendComposerToRoomAsync(
            new WiredEnvironmentMessageComposer
            {
                HasClickUserWired = clickUser.Present,
                EnabledAchievements = await room.GetEnabledAchievementsAsync(ct),
            }
        );

        return true;
    }
}
