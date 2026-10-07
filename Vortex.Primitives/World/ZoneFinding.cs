namespace Vortex.Primitives.World;

/// <summary>One validation result; <paramref name="ElementId"/> lets the editor centre on it.</summary>
public sealed record ZoneFinding(
    ZoneSeverity Severity,
    string Code,
    string? ElementId,
    string Message
);
