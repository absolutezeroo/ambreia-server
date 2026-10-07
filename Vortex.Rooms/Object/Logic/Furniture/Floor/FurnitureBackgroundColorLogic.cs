using System.Threading.Tasks;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Furniture.StuffData;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Object.Logic;

namespace Vortex.Rooms.Object.Logic.Furniture.Floor;

/// <summary>
/// The background toner: tints the whole room a colour its owner picks in HSL.
/// </summary>
/// <remarks>
/// Four numbers in one array, read by position on the client's side — state, hue, saturation,
/// lightness — which is why these three definitions are the catalogue's only number-array furni.
/// The toner's on/off is the ordinary state at index 0, not a separate concept: its widget sends a
/// plain <c>UseFurniture</c> for the switch and a separate packet only for the colour, so the
/// inherited toggle is exactly right and is deliberately left alone. The colour packet has no
/// handler since the cut of 2026-10-07; nothing calls <see cref="SetColorAsync"/> yet.
/// </remarks>
[RoomObjectLogic("furniture_background_color")]
public class FurnitureBackgroundColorLogic(
    IStuffDataFactory stuffDataFactory,
    IRoomFloorItemContext ctx
) : FurnitureFloorLogic(stuffDataFactory, ctx)
{
    private const int HueIndex = 1;
    private const int SaturationIndex = 2;
    private const int LightnessIndex = 3;
    private const int FieldCount = 4;

    /// <summary>Hue is a full turn, the other two are percentages — the widget's own slider ranges.
    /// Clamped rather than trusted: the sliders stay in range but the client's packet is four bare
    /// ints.</summary>
    public Task SetColorAsync(int hue, int saturation, int lightness)
    {
        if (StuffData is not INumberStuffData numbers)
        {
            return Task.CompletedTask;
        }

        // A toner that has never been set carries only its state, so the colour slots have to be
        // made before they can be written; assigning past the end would throw on first use.
        while (numbers.Data.Count < FieldCount)
        {
            numbers.Data.Add(0);
        }

        numbers.Data[HueIndex] = System.Math.Clamp(hue, 0, 360);
        numbers.Data[SaturationIndex] = System.Math.Clamp(saturation, 0, 100);
        numbers.Data[LightnessIndex] = System.Math.Clamp(lightness, 0, 100);

        return PersistStuffDataAsync();
    }
}
