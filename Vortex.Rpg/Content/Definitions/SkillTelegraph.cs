namespace Vortex.Rpg.Content.Definitions;

/// <summary>
/// A cast that marks its ground first (spec M1c §5.3, §8): the cells are fixed when the cast starts,
/// and when it lands it touches whoever still stands on them. The delay is the cast time.
/// </summary>
public sealed record SkillTelegraph
{
    public required TelegraphShape Shape { get; init; }

    /// <summary>Radius of a circle, length of a line or a cone: 1 to 10.</summary>
    public required int Size { get; init; }

    /// <summary>A line's width: odd, 1 to 5.</summary>
    public int Width { get; init; } = 1;

    /// <summary>A cone's opening in degrees: 30 to 180.</summary>
    public int Angle { get; init; } = 90;
}
