namespace Vortex.Primitives.Feed;

/// <summary>What a feed post carries under its text.</summary>
public enum FeedPostKind
{
    /// <summary>Text alone.</summary>
    Status = 0,

    /// <summary>One to four of the author's own published camera photos.</summary>
    Photo = 1,

    /// <summary>One question and two to five choices.</summary>
    Poll = 2,
}
