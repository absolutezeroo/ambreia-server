namespace Vortex.Furniture.Assets;

/// <summary>
/// Where one named asset sits on a bundle's sprite sheet, and how it is turned before it is drawn.
/// </summary>
/// <param name="X">Left edge of the crop, in sheet pixels.</param>
/// <param name="Y">Top edge of the crop, in sheet pixels.</param>
/// <param name="Width">Crop width.</param>
/// <param name="Height">Crop height.</param>
/// <param name="FlipH">
/// The asset is the mirror of the frame it points at. Most furniture ships four directions and
/// mirrors the other four rather than drawing them, so this is the common case, not an oddity.
/// </param>
/// <param name="FlipV">Same, vertically. Rare, and present in the format, so carried.</param>
/// <remarks>
/// Deliberately not a drawing offset. The camera payload's own <c>x</c>/<c>y</c> come from
/// <c>sprite.offsetX</c>/<c>offsetY</c>, which the client's visualisation had already resolved
/// through the asset — the offset is spent by the time it reaches the wire, and applying the
/// manifest's copy again would move every sprite twice.
/// </remarks>
public readonly record struct NitroSpriteFrame(
    int X,
    int Y,
    int Width,
    int Height,
    bool FlipH,
    bool FlipV
);
