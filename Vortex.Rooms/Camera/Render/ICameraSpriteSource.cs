using System;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Camera;

namespace Vortex.Rooms.Camera.Render;

/// <summary>
/// Where a sprite's encoded bytes come from: the hotel's extracted asset directory, or an allowed
/// url.
/// </summary>
/// <remarks>
/// <para>
/// Bytes, not pixels. Decoding belongs to whoever is drawing, and keeping this boundary at "an
/// encoded image or nothing" is what lets the policy — which names may be read, which hosts may be
/// reached, how many times — be tested without a raster library anywhere near it.
/// </para>
/// <para>
/// The two branches are not interchangeable and the caller does not choose between them: the scene
/// decoder already classified every sprite as a local key or an allowed url, and this follows that
/// classification rather than re-deciding it.
/// </para>
/// </remarks>
public interface ICameraSpriteSource
{
    /// <summary>
    /// The encoded image for one sprite, or empty when there is none.
    /// </summary>
    /// <param name="source">
    /// Which branch to take. An <see cref="CameraSpriteSource.ExternalImage"/> name has already
    /// passed the operator's allowlist; a <see cref="CameraSpriteSource.LocalAsset"/> name is a key,
    /// never a path.
    /// </param>
    ValueTask<ReadOnlyMemory<byte>> ReadAsync(
        string name,
        CameraSpriteSource source,
        CancellationToken ct
    );

    /// <summary>
    /// Starts a fresh network budget for one photo.
    /// </summary>
    /// <remarks>
    /// A scene may name dozens of external images and each one is a request this hotel makes. The
    /// allowlist says <em>where</em> those may go; this bounds <em>how many</em> and for how long,
    /// so one photo cannot become a burst against the asset host — or a render that never finishes
    /// because a host stopped answering.
    /// </remarks>
    void BeginPhoto();
}
