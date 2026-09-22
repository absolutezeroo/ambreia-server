using System;
using System.Collections.Immutable;
using System.Linq;

namespace Vortex.Primitives.Navigator;

/// <summary>
/// The promoted-room group codes the official client can put a name to.
/// </summary>
/// <remarks>
/// <para>
/// <c>PromotedRoomsListCtrl</c> titles a group with
/// <c>getText("promotedroomcategory." + code)</c> and has no fallback, so a group sent under any
/// other code renders with the raw key — <c>promotedroomcategory.myOwnCode</c> — as its heading.
/// These fifteen are every such key present in the client's <c>external_texts.json</c>, which is
/// why the editor offers them as a list instead of a text box.
/// </para>
/// <para>
/// The English wording beside each one is the client's own, kept here as the operator-facing hint;
/// the client localizes it per language and never reads this copy.
/// </para>
/// </remarks>
public static class NavigatorPromotedCategoryCodes
{
    /// <summary>How many rooms a group carries. The client's own labels for the expander are
    /// <c>navigator.promotedrooms.viewtopten</c> / <c>hidetopten</c>, so a group is a top ten and
    /// sending more only makes the list disagree with the button that opens it.</summary>
    public const int GroupSize = 10;

    public static readonly ImmutableArray<(string Code, string ClientText)> Known =
    [
        ("africaDesert", "Egyptian Temple Competition Winners"),
        ("africaJungle", "Tree-Fort Competition Winners"),
        ("africaSavannah", "Wildlife Reserve Competition Winners"),
        ("battleBallRoom", "Play Battle Banzai"),
        ("freezeRoom", "Play Freeze"),
        ("friendlyRoom", "Find friends"),
        ("majesty", "Her Majesty's Secret Service"),
        ("partyRoom", "Party hard"),
        ("petRoom", "Show-off your pet"),
        ("starsRoomComp", "Zabbo Stars"),
        ("starsRoomComp2", "Zabbo Stars"),
        ("tradingRoom", "Trade and Giveaways"),
        ("vipParties1", "Yacht Parties"),
        ("vipParties2", "Movie Premieres"),
        ("vipParties3", "Vault Parties"),
    ];

    public static bool IsKnown(string code) =>
        Known.Any(entry => string.Equals(entry.Code, code, StringComparison.Ordinal));
}
