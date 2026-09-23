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
using Vortex.Rooms.Wired;
using Vortex.Rooms.Wired.Rules;

namespace Vortex.Rooms.Object.Logic.Furniture.Floor.Wired.Addons;

/// <summary>
/// "WIRED Add-on: Global Placeholder" (addon code 2000) — a named <c>$(name)</c> that every text
/// the room's wired says is put through, set either to a value or to a placeholder another of the
/// owner's rooms defines.
/// </summary>
/// <remarks>
/// <para>
/// The dialog (<c>GlobalPlaceholderAddon.as</c>) sends [type, 0, room id] and "name\tvalue", where
/// the value is the text itself for type 0 and the other room's placeholder name for type 1.
/// </para>
/// <para>
/// Derived, with no official behaviour to check against: "global" is read as room-wide — unlike the
/// username and furni-name placeholders it names no selection, so there is nothing tying it to its
/// own pile — and every action's text goes through the room's global placeholders after its pile's
/// own add-ons (<c>FurnitureWiredActionLogic.ApplyTextAddonsAsync</c>). A value from another room is
/// read off that room's stored row (<see cref="WiredGlobalPlaceholderIndex"/>) and kept for
/// <see cref="ReferenceTtlMs"/>, so a chatty timer does not query the database on every line.
/// </para>
/// <para>
/// The logic key is derived: no furnidata in any tree has this add-on.
/// </para>
/// </remarks>
[RoomObjectLogic("wf_xtra_global_placeholder")]
public class WiredAddonGlobalPlaceholder(
    IGrainFactory grainFactory,
    IStuffDataFactory stuffDataFactory,
    IRoomFloorItemContext ctx
) : FurnitureWiredAddonLogic(grainFactory, stuffDataFactory, ctx)
{
    public override int WiredCode => (int)WiredAddonType.GLOBAL_PLACEHOLDER;

    private const int FromAnotherRoom = 1;

    private const long ReferenceTtlMs = 30_000;

    private string? _cachedReference;

    private long _cachedAtMs = long.MinValue;

    public override List<IWiredParamRule> GetIntParamRules() =>
        [
            new WiredRangeParamRule(0, 1, 0), // 0 value / from another room
            new WiredParamRule(0), // 1 unused, always 0
            new WiredParamRule(0), // 2 the other room's id
        ];

    /// <summary>"name\tvalue", split once: a value may itself contain tabs.</summary>
    public static (string Name, string Value) ParseConfiguration(string? stringParam)
    {
        string text = stringParam ?? string.Empty;
        int tab = text.IndexOf('\t');

        return tab < 0 ? (text.Trim(), string.Empty) : (text[..tab].Trim(), text[(tab + 1)..]);
    }

    /// <summary>The token this box answers to and what it stands for, or null when it is unnamed.
    /// A reference whose room no longer defines the name stands for nothing.</summary>
    public async Task<(string Token, string Value)?> ResolveAsync(CancellationToken ct)
    {
        await LoadWiredAsync(ct);

        (string name, string value) = ParseConfiguration(_wiredData.StringParam);
        string token = WiredPlaceholder.BuildToken(name);

        if (token.Length == 0)
        {
            return null;
        }

        if (Param(0) != FromAnotherRoom)
        {
            return (token, value);
        }

        long now = _ctx.NowMs();

        if (now - _cachedAtMs >= ReferenceTtlMs)
        {
            _cachedReference = await _ctx.RoomAs<IRoomWired>()
                .ResolveSharedPlaceholderAsync(Param(2), value, ct);
            _cachedAtMs = now;
        }

        return (token, _cachedReference ?? string.Empty);
    }

    /// <summary>A save may repoint the reference, so the cached value goes with it.</summary>
    public override Task<bool> ApplyWiredUpdateAsync(
        ActionContext ctx,
        WiredUpdateRequest update,
        CancellationToken ct
    )
    {
        _cachedAtMs = long.MinValue;

        return base.ApplyWiredUpdateAsync(ctx, update, ct);
    }

    private int Param(int index) =>
        index < _wiredData.IntParams.Count ? _wiredData.IntParams[index] : 0;
}
