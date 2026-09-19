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

    /// <summary>
    /// Where furniture bundles are served, ending in a slash. Carried here rather than fixed when the
    /// library is built, so an operator moving their asset host takes effect on the next photo rather
    /// than on the next restart — the same way every other number in this record does.
    /// </summary>
    [Id(4)]
    public required string FurnitureAssetUrl { get; init; }

    /// <summary>Where pet and avatar bundles are served — the client's versioned gordon directory.</summary>
    [Id(5)]
    public required string AvatarAssetUrl { get; init; }

    /// <summary>The client's figuremap, which says which library an avatar part lives in.</summary>
    [Id(6)]
    public required string FiguremapUrl { get; init; }

    /// <summary>
    /// How many bundles one photo may fetch. Furniture is checked against the catalogue first and
    /// costs nothing to refuse; a pet or an avatar part cannot be, so this is their bound.
    /// </summary>
    [Id(7)]
    public required int MaxBundleFetches { get; init; }

    public static CameraRenderLimits Defaults { get; } =
        new()
        {
            MaxDimension = CameraConfig.DefaultMaxPhotoDimension,
            MaxExternalFetches = CameraConfig.DefaultMaxExternalFetches,
            ExternalTimeout = TimeSpan.FromSeconds(CameraConfig.DefaultExternalTimeoutSeconds),
            MaxExternalBytes = CameraConfig.DefaultMaxExternalBytes,
            FurnitureAssetUrl = CameraConfig.DefaultFurnitureAssetUrl,
            AvatarAssetUrl = CameraConfig.DefaultAvatarAssetUrl,
            FiguremapUrl = CameraConfig.DefaultFiguremapUrl,
            MaxBundleFetches = CameraConfig.DefaultMaxBundleFetches,
        };
}
