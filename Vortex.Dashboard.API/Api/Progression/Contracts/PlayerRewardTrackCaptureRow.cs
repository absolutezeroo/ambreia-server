namespace Vortex.Dashboard.API.Api.Progression.Contracts;

/// <summary>
/// One fact a satisfied step recorded, and which step recorded it.
/// </summary>
/// <param name="Step">Zero-based, as the engine counts. Rendered one-based, as an operator reads.</param>
/// <remarks>
/// Decoded here rather than sent as the row holds it: the stored form is separated by ASCII
/// control characters, which arrive on screen as a line of replacement boxes. The dashboard should
/// not have to know the engine's storage to say what a player has matched.
/// </remarks>
public sealed record PlayerRewardTrackCaptureRow(int Step, string FactKey, string Value);
