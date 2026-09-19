namespace Vortex.Primitives.Camera;

/// <summary>Where a sprite's pixels come from, decided once at decode time.</summary>
/// <remarks>
/// The client writes both kinds into the same <c>name</c> field, which is exactly why the decision
/// belongs here and not in the renderer: a renderer that classifies as it draws is a renderer that
/// can be argued into a network call by a crafted name.
/// </remarks>
public enum CameraSpriteSource
{
    /// <summary>A key in the hotel's own asset index.</summary>
    LocalAsset = 0,

    /// <summary>An absolute url that passed the operator's allowlist.</summary>
    ExternalImage = 1,
}
