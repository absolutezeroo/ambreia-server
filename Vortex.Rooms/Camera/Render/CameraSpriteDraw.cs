namespace Vortex.Rooms.Camera.Render;

/// <summary>
/// How one sprite is put on the canvas: where, and everything that changes how it looks.
/// </summary>
/// <remarks>
/// Separate from <see cref="Vortex.Primitives.Camera.CameraSceneSprite"/> because the scene is what
/// the client said and this is what the renderer decided. They agree today on most fields; they stop
/// agreeing the moment the renderer resolves anything for itself — a palette, a default, a clamp.
/// </remarks>
public readonly record struct CameraSpriteDraw
{
    public required double X { get; init; }

    public required double Y { get; init; }

    /// <summary>0–255.</summary>
    public required int Alpha { get; init; }

    public required bool FlipH { get; init; }

    /// <summary>Horizontal shear, in the scene's own units. Zero for almost every sprite.</summary>
    public required double Skew { get; init; }

    /// <summary>A tint to multiply the sprite by, or null to draw it as it is.</summary>
    public required int? Color { get; init; }

    /// <summary>The client's own blend name, or null for <c>normal</c>.</summary>
    public required string? BlendMode { get; init; }
}
