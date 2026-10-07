using System.Text.Json;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Object.Logic;
using Vortex.Primitives.Rooms.Object.Logic.Furniture;

namespace Vortex.Rooms.Object.Logic.Furniture.Floor;

/// <summary>
/// The magic stack tile: a floor item whose altitude the owner sets by hand, and which everything
/// else then stacks on top of. Carried by 22 definitions in this catalogue — the
/// <c>tile_stackmagic*</c> family and the <c>tile_walk_magic*</c> one.
/// </summary>
/// <remarks>
/// The tile holds no height of its own: every definition in the family declares
/// <c>stack_height 0</c>, and <c>RoomItem.Height</c> is <c>Z + GetStackHeight()</c>, so writing the
/// chosen height into the item's Z is what makes the tile carry furniture at that altitude. That is
/// also what the widget reads back — <c>CustomStackHeightWidget</c> displays
/// <c>roomObject.getLocation().z</c>, not stuff data — so the height has exactly one home and the
/// dialog cannot disagree with the room.
/// <para>
/// No packet handler sets the height or <see cref="MultiWalk"/> since the cut of 2026-10-07; only
/// tests call <see cref="SetMultiWalk"/>. Registering the name still keeps these tiles from
/// resolving through the provider's fallback to <see cref="FurnitureFloorLogic"/>.
/// </para>
/// </remarks>
[RoomObjectLogic("furniture_custom_stack_height")]
public class FurnitureCustomStackHeightLogic(
    IStuffDataFactory stuffDataFactory,
    IRoomFloorItemContext ctx
) : FurnitureFloorLogic(stuffDataFactory, ctx)
{
    private bool? _multiWalk;

    /// <summary>
    /// Whether avatars may walk on every level of the stack rather than only its top. Persisted in
    /// its own extra-data section: the wire carries it in the floor item's <c>extra</c> field, which
    /// is not stuff data and has no state number to live in.
    /// </summary>
    public bool MultiWalk
    {
        get => _multiWalk ??= LoadMultiWalk();
        private set => _multiWalk = value;
    }

    /// <summary>
    /// The floor item's <c>extra</c> field. The client mirrors it into <c>furniture_extra</c> and
    /// the widget's checkbox reads it from there, so this is how a reopened dialog remembers what
    /// was ticked.
    /// </summary>
    public override int GetExtra() => MultiWalk ? 1 : 0;

    public void SetMultiWalk(bool multiWalk)
    {
        MultiWalk = multiWalk;

        _ctx.RoomObject.ExtraData.UpdateSection(ExtraDataSectionType.MAGIC_TILE, multiWalk);
    }

    private bool LoadMultiWalk() =>
        _ctx.RoomObject.ExtraData.TryGetSection(
            ExtraDataSectionType.MAGIC_TILE,
            out JsonElement element
        )
        && element.ValueKind == JsonValueKind.True;
}
