namespace Vortex.Primitives.Rpg.Content;

/// <summary>Every definition of one public kind, as a JSON array in the shape of the content files,
/// at the content version that served it (the client's cache key).</summary>
public sealed record ContentDefinitions(long Version, string Json);
