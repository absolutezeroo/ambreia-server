namespace Vortex.Primitives.Habbicons;

/// <summary>
/// What a player's relationship to one Habbicon is, in the client's own numbering.
/// </summary>
/// <remarks>
/// <para>
/// Read from <c>HabbiconView.as</c>, which derives every flag it renders from this one integer:
/// <c>favorite = state == 3</c>, <c>owned = favorite || state == 2</c>,
/// <c>claimable = state == 1</c>, <c>purchasable = state == 0 &amp;&amp; hasPrice</c>. The controller
/// additionally treats 1, 2 and 3 as "the server keeps a row for this" (<c>isStoredUserState</c>)
/// and 1 → 2|3 as "a claimable reward was just claimed", which is what fires its "new Habbicon"
/// notification.
/// </para>
/// <para>
/// The client's <c>HabbiconState</c> class also declares 4 and 5 (<c>REWARD</c>); nothing in the
/// decompiled tree reads either as a wire value, so they are deliberately absent here rather than
/// guessed at. See the protocol note in
/// <c>docs/walkthroughs/habbicons-and-reward-tracks.md</c>.
/// </para>
/// </remarks>
public enum HabbiconState
{
    /// <summary>Not owned. Buyable when the shop row carries a price.</summary>
    NotOwned = 0,

    /// <summary>
    /// Unlocked but not yet taken — the state a collection's bonus Habbicon sits in once the
    /// collection is complete and before the player claims it.
    /// </summary>
    Claimable = 1,

    /// <summary>Owned.</summary>
    Owned = 2,

    /// <summary>Owned and marked as a favourite. Still owned; the client folds the two.</summary>
    Favourite = 3,
}
