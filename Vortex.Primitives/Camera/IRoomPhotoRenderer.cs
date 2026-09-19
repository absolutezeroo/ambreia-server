using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Primitives.Camera;

/// <summary>
/// Turns an accepted scene into the PNG the client will fetch.
/// </summary>
/// <remarks>
/// The seam exists so the grain does not know how a photo is drawn, and so the drawing can be
/// replaced without touching the intake that decides what is drawable. Everything it is handed has
/// already been validated: counts are inside the hotel's limits, planes have four corners, and every
/// sprite is either a local asset key or a url an operator allowed.
/// </remarks>
public interface IRoomPhotoRenderer
{
    /// <summary>
    /// Draws <paramref name="scene"/> and stores it under <paramref name="photoKey"/>.
    /// </summary>
    /// <returns>
    /// False when the photo could not be produced. The player is told the same thing either way —
    /// an empty url — so the reason belongs in the log, not in the return.
    /// </returns>
    Task<bool> RenderAsync(CameraScene scene, string photoKey, CancellationToken ct);
}
