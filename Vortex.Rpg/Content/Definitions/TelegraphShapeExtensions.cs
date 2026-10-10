namespace Vortex.Rpg.Content.Definitions;

/// <summary>What a telegraph shape means for its aim (spec M1c §8.1), in one place for the import and
/// the cast.</summary>
public static class TelegraphShapeExtensions
{
    /// <summary>A circle sits on what it aims at; a line or a cone leaves the caster toward it, so it
    /// needs something to aim at (plan M1c decision 4).</summary>
    public static bool CentresOnAim(this TelegraphShape shape) => shape == TelegraphShape.Circle;
}
