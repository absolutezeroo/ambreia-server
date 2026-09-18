namespace Vortex.Primitives.Navigator.Enums;

/// <summary>
/// What a result block's header offers, using the client's own names
/// (<c>NavigatorSearchAction</c> in <c>NavigatorSearchResultBlock</c>).
/// <para>
/// <c>0</c> does not mean "collapsed" -- an earlier <c>Collapsed</c>/<c>Expanded</c> naming said so
/// and it was wrong. <c>CategoryElementFactory</c> reads this field as
/// <c>back.visible = (v == 2)</c>, <c>collapse.visible = (v != 2)</c>,
/// <c>showMore.visible = (v == 1)</c>: the collapse arrow is on for everything but
/// <see cref="Back"/>, and whether a block *starts* closed is the separate <c>forceClosed</c> flag.
/// </para>
/// </summary>
public enum NavigatorActionAllowedType
{
    /// <summary>
    /// Collapse arrow only. What the official hotel sends on every block of a tab overview
    /// (capture of <c>official_view</c> on Habbo.fr: all seven blocks carry <c>0</c>).
    /// </summary>
    None = 0,

    /// <summary>
    /// Adds a "show more" link. The client re-searches the block's own search code with no filter,
    /// so only use it where that second search returns the same list, unclipped.
    /// </summary>
    ShowMore = 1,

    /// <summary>
    /// Replaces the collapse arrow with a "back" link. The end of a drill-down, where there is
    /// nothing left to expand into.
    /// </summary>
    Back = 2,
}
