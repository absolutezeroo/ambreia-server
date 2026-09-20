namespace Vortex.Primitives.Rooms.Enums;

/// <summary>
/// Where somebody is standing in relation to a piece of furniture, from the furniture's own point of
/// view.
/// </summary>
/// <remarks>
/// Relative, never compass: a chair turned round takes its front with it, so "in front of the
/// television" keeps meaning that after a builder rearranges the room. An absolute direction would
/// have made every piece of content depend on how the room happened to be laid out the day it was
/// written.
/// <para>
/// The string values are what a signal carries and what content stores, so they are spelled here
/// once and never restated.
/// </para>
/// </remarks>
public enum RelativePosition
{
    /// <summary>Standing on the furniture itself.</summary>
    On = 0,

    /// <summary>On the tile the furniture faces.</summary>
    Front = 1,

    FrontRight = 2,
    Right = 3,
    BackRight = 4,

    /// <summary>Directly behind it: the tile it has its back to.</summary>
    Back = 5,

    BackLeft = 6,
    Left = 7,
    FrontLeft = 8,
}
