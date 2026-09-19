namespace Vortex.Rooms.Camera.Render;

/// <summary>
/// Which bundle a sprite name lives in, and where that bundle is served.
/// </summary>
/// <param name="Url">The full url of the <c>.nitro</c>.</param>
/// <param name="Bundle">Its name, which is also the cache key and the frame-key prefix.</param>
internal readonly record struct NitroBundleAddress(string Url, string Bundle);
