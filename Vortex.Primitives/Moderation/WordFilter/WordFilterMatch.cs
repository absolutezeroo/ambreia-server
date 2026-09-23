namespace Vortex.Primitives.Moderation.WordFilter;

/// <summary>How a listed word is found in a text.</summary>
public enum WordFilterMatch
{
    /// <summary>Only on word boundaries: "con" does not match inside "concert".</summary>
    WholeWord = 0,

    /// <summary>Anywhere, including inside a longer word.</summary>
    Partial = 1,
}
