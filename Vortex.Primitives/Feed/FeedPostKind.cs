namespace Vortex.Primitives.Feed;

/// <summary>What a feed post carries under its text.</summary>
public enum FeedPostKind
{
    /// <summary>Text alone.</summary>
    Status = 0,

    // 1 was Photo, removed with the camera (M0). Not renumbered: the kind is stored on each post.

    /// <summary>One question and two to five choices.</summary>
    Poll = 2,

    // 3 was Album, removed with the camera's photo albums (M0).
}
