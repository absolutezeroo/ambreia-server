using Orleans;

namespace Vortex.Primitives.Camera;

/// <summary>
/// One drawn sprite: a furniture part, an avatar limb, a badge, a photo hanging on a wall.
/// </summary>
/// <remarks>
/// <see cref="Name"/> is an asset key when <see cref="Source"/> is
/// <see cref="CameraSpriteSource.LocalAsset"/>, and an absolute url when it is
/// <see cref="CameraSpriteSource.ExternalImage"/> — one that has already been checked against the
/// operator's allowlist. A sprite whose name is neither does not reach this type: the decoder drops
/// it, which is the same thing the client does to its own once it has more than thirty external
/// images in one shot.
/// </remarks>
[GenerateSerializer, Immutable]
public sealed record CameraSceneSprite
{
    [Id(0)]
    public required string Name { get; init; }

    [Id(1)]
    public required CameraSpriteSource Source { get; init; }

    [Id(2)]
    public required double X { get; init; }

    [Id(3)]
    public required double Y { get; init; }

    [Id(4)]
    public required double Z { get; init; }

    /// <summary>0–255. Absent from the payload means opaque; the client only writes it when it is
    /// not 255.</summary>
    [Id(5)]
    public required int Alpha { get; init; }

    [Id(6)]
    public required bool FlipH { get; init; }

    [Id(7)]
    public required double Skew { get; init; }

    [Id(8)]
    public required int Frame { get; init; }

    /// <summary>A tint, or null when the sprite is drawn as it is.</summary>
    [Id(9)]
    public required int? Color { get; init; }

    /// <summary>The client's own name for the blend, or null for <c>normal</c>.</summary>
    [Id(10)]
    public required string? BlendMode { get; init; }

    /// <summary>Written by the client only for an external image, which has no intrinsic size the
    /// server can know without fetching it.</summary>
    [Id(11)]
    public required int Width { get; init; }

    [Id(12)]
    public required int Height { get; init; }

    /// <summary>The palette an avatar part is recoloured through, or null.</summary>
    [Id(13)]
    public required string? PaletteSourceName { get; init; }
}
