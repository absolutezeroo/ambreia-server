namespace Vortex.Primitives.Moderation.WordFilter;

/// <summary>What the hotel-wide filter does with a text containing a listed word.</summary>
public enum WordFilterAction
{
    /// <summary>The text goes through with the offending span replaced.</summary>
    Replace = 0,

    /// <summary>The whole text is refused and the player is told why.</summary>
    Refuse = 1,
}
