namespace Vortex.Rooms.Camera.Render;

/// <summary>
/// The photo's pixel size, derived from the scene rather than declared by it.
/// </summary>
/// <remarks>
/// A record struct so a test can state the whole expectation in one comparison, and so the value
/// cannot drift between the surface that was allocated and the surface that was drawn on.
/// </remarks>
public readonly record struct CameraViewport(int Width, int Height);
