namespace Vortex.Primitives.World.Editing;

/// <summary>One furniture of the editor's palette, with the collision defaults it starts from.</summary>
public sealed record FurniturePaletteEntry(
    int Definition,
    string Class,
    bool Wall,
    int Width,
    int Length,
    int States,
    bool CanWalk,
    bool CanSit,
    double StackHeight
);
