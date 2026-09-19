using System;
using Orleans;

namespace Vortex.Primitives.Camera;

/// <summary>
/// What the rasteriser is allowed to spend on one photo.
/// </summary>
/// <remarks>
/// Passed in rather than read inside, for the same reason <see cref="CameraSceneLimits"/> is: the
/// grain has already resolved the camera's config to decide whether to accept the scene at all, and
/// reading it a second time would be a round trip to learn what the caller knows — and a second
/// place for the numbers to come from.
/// </remarks>
[GenerateSerializer, Immutable]
public sealed record CameraRenderLimits
{
    /// <summary>
    /// Ceiling on either side of the photo. The payload never says how big it is, and nothing
    /// bounds a coordinate, so this is what stands between a corner at a billion and an allocation
    /// that fails.
    /// </summary>
    [Id(0)]
    public required int MaxDimension { get; init; }

    /// <summary>
    /// How many images one photo may fetch. The allowlist says where; this says how often, so one
    /// photo cannot become a burst against the asset host.
    /// </summary>
    [Id(1)]
    public required int MaxExternalFetches { get; init; }

    [Id(2)]
    public required TimeSpan ExternalTimeout { get; init; }

    /// <summary>Largest external image accepted, enforced while reading rather than from the
    /// sender's own Content-Length.</summary>
    [Id(3)]
    public required int MaxExternalBytes { get; init; }

    public static CameraRenderLimits Defaults { get; } =
        new()
        {
            MaxDimension = CameraConfig.DefaultMaxPhotoDimension,
            MaxExternalFetches = CameraConfig.DefaultMaxExternalFetches,
            ExternalTimeout = TimeSpan.FromSeconds(CameraConfig.DefaultExternalTimeoutSeconds),
            MaxExternalBytes = CameraConfig.DefaultMaxExternalBytes,
        };
}
