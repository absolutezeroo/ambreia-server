using System;
using System.Collections.Immutable;
using Orleans;

namespace Vortex.Primitives.Camera;

/// <summary>
/// A room photograph as the client described it: everything the renderer needs and nothing it has to
/// re-parse.
/// </summary>
/// <remarks>
/// This is the validated form. Every count is already inside the hotel's limits, every plane has its
/// four corners, and every sprite has been classified as a local asset key or an allowed url. A
/// renderer handed one of these cannot be steered anywhere the decoder did not already agree to go,
/// which is the point of parsing into a type rather than walking the JSON as it draws.
/// </remarks>
[GenerateSerializer, Immutable]
public sealed record CameraScene
{
    [Id(0)]
    public required int RoomId { get; init; }

    /// <summary>1 unless the player zoomed out. The client omits it at 1.</summary>
    [Id(1)]
    public required int Zoom { get; init; }

    [Id(2)]
    public required ImmutableArray<CameraScenePlane> Planes { get; init; }

    [Id(3)]
    public required ImmutableArray<CameraSceneSprite> Sprites { get; init; }

    /// <summary>The camera effects the player turned on, as the client's own json — a list of
    /// <c>{name, alpha}</c>. Kept verbatim: the renderer owns what an effect means, and re-modelling
    /// it here would be a second place to keep that list correct.</summary>
    [Id(4)]
    public required string EffectsJson { get; init; }

    /// <summary>When the client says it took the shot, already checked against the freshness
    /// window.</summary>
    [Id(5)]
    public required DateTime TakenAtUtc { get; init; }

    /// <summary>How many sprites were dropped for naming a url the operator does not allow. Zero on
    /// an honest client; anything else is worth a log line and, repeated, worth a look.</summary>
    [Id(6)]
    public required int RejectedExternalSprites { get; init; }
}
