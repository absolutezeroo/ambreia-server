using System;

namespace Vortex.Rooms.Wired.Projectile;

/// <summary>
/// One slide the projectile add-on shaped, as the client draws it: where it started, how far it
/// goes on screen (overshoot included), and how long it takes.
/// </summary>
/// <remarks>
/// The client moves the furni linearly over <see cref="DurationMs"/>, from the source to source +
/// visual delta (<c>MovingObjectLogic.update()</c>, the delta stretched by
/// <c>fixDeltaAndIntervalForOvershooting()</c>), so everything here is a straight interpolation on
/// that. The furni itself is already standing on its target server-side; this is only the flight
/// the room watches. <see cref="PathSteps"/> is the stretch it really crossed — from its source to
/// its target — which is where collisions are counted: an overshoot is drawn past the target and
/// may run through a wall.
/// </remarks>
internal sealed record ProjectileFlight(
    long StartMs,
    int DurationMs,
    int SourceX,
    int SourceY,
    double SourceZ,
    double VisualDeltaX,
    double VisualDeltaY,
    double DeltaZ,
    int VisualSteps,
    int PathDeltaX,
    int PathDeltaY
)
{
    public int PathSteps => Math.Max(Math.Abs(PathDeltaX), Math.Abs(PathDeltaY));

    public bool IsTravelling(long nowMs) => nowMs < StartMs + DurationMs;

    public double Progress(long nowMs) =>
        DurationMs <= 0 ? 1 : Math.Clamp((nowMs - StartMs) / (double)DurationMs, 0, 1);

    /// <summary>Whole tiles covered so far, overshoot included.</summary>
    public int TilesTravelled(long nowMs) => (int)Math.Floor(Progress(nowMs) * VisualSteps);

    public int X(long nowMs) => (int)Math.Round(SourceX + (VisualDeltaX * Progress(nowMs)));

    public int Y(long nowMs) => (int)Math.Round(SourceY + (VisualDeltaY * Progress(nowMs)));

    /// <summary>Hundredths of a tile, the unit altitude variables are kept in.</summary>
    public int Altitude(long nowMs) =>
        (int)Math.Round((SourceZ + (DeltaZ * Progress(nowMs))) * 100);

    /// <summary>The tile of the real path the projectile is over after <paramref name="step"/>
    /// steps (0 is its source).</summary>
    public (int X, int Y) PathTile(int step) =>
        PathSteps == 0
            ? (SourceX, SourceY)
            : (
                (int)Math.Round(SourceX + (PathDeltaX * step / (double)PathSteps)),
                (int)Math.Round(SourceY + (PathDeltaY * step / (double)PathSteps))
            );

    /// <summary>How many steps of the real path it has crossed so far.</summary>
    public int PathStepsCrossed(long nowMs) =>
        Math.Min(PathSteps, (int)Math.Floor(Progress(nowMs) * VisualSteps));
}
