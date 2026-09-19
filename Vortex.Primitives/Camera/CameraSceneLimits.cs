using System.Collections.Immutable;
using Orleans;

namespace Vortex.Primitives.Camera;

/// <summary>
/// The ceilings the scene decoder enforces, resolved once from <see cref="CameraConfig"/> and passed
/// in rather than read inside. Keeping them an argument is what makes the decoder a pure function and
/// therefore testable at its edges: a limit that can only be reached through a live config grain is a
/// limit nobody writes a test for.
/// </summary>
[GenerateSerializer, Immutable]
public sealed record CameraSceneLimits
{
    [Id(0)]
    public required int MaxPayloadBytes { get; init; }

    [Id(1)]
    public required int MaxSceneBytes { get; init; }

    [Id(2)]
    public required int MaxPlanes { get; init; }

    [Id(3)]
    public required int MaxSprites { get; init; }

    [Id(4)]
    public required int MaxMasksPerPlane { get; init; }

    [Id(5)]
    public required int MaxTextureColumnsPerPlane { get; init; }

    [Id(6)]
    public required int TimestampSkewSeconds { get; init; }

    /// <summary>
    /// The url prefixes an external sprite may name. Empty means no sprite is ever fetched over the
    /// network, which is the default and the only setting that is safe without an operator having
    /// thought about it.
    /// </summary>
    [Id(7)]
    public required ImmutableArray<string> ExternalImagePrefixes { get; init; }

    /// <summary>The compiled defaults, for a caller with no config grain in reach — tests, and the
    /// fallback path when a key is unset.</summary>
    public static CameraSceneLimits Defaults { get; } =
        new()
        {
            MaxPayloadBytes = CameraConfig.DefaultMaxPayloadBytes,
            MaxSceneBytes = CameraConfig.DefaultMaxSceneBytes,
            MaxPlanes = CameraConfig.DefaultMaxPlanes,
            MaxSprites = CameraConfig.DefaultMaxSprites,
            MaxMasksPerPlane = CameraConfig.DefaultMaxMasksPerPlane,
            MaxTextureColumnsPerPlane = CameraConfig.DefaultMaxTextureColumnsPerPlane,
            TimestampSkewSeconds = CameraConfig.DefaultTimestampSkewSeconds,
            ExternalImagePrefixes = [],
        };
}
