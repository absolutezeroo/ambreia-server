namespace Vortex.Primitives.Quests;

/// <summary>
/// Canonical quest objective type names (the <c>quest_type</c> column). A quest advances when a
/// progression trigger fires with the matching type. Kept in Vortex.Primitives so both the room/
/// packet handlers and the player-side quest handlers reference the same constants.
/// </summary>
public static class QuestTypes
{
    /// <summary>The player entered a room.</summary>
    public const string RoomEntry = "RoomEntry";

    /// <summary>The player's friend list grew (an accepted friend request, both sides).</summary>
    public const string FriendListSize = "FriendListSize";

    /// <summary>
    /// The player sent a friend request. Distinct from <see cref="FriendListSize"/>, which needs the
    /// other side to accept: this one advances on the asking alone, which is what the client's own
    /// "ask for a friend" quest step reports.
    /// </summary>
    public const string FriendRequestSent = "FriendRequestSent";

    /// <summary>The player changed their avatar figure (look).</summary>
    public const string AvatarLooks = "AvatarLooks";

    /// <summary>The player sent a chat message.</summary>
    public const string Chat = "Chat";

    /// <summary>The player waved.</summary>
    public const string Wave = "Wave";

    /// <summary>The player danced.</summary>
    public const string Dance = "Dance";

    /// <summary>The player logged in.</summary>
    public const string Login = "Login";

    /// <summary>The player played a room game (no trigger wired yet).</summary>
    public const string GamePlayed = "GamePlayed";

    /// <summary>A catalog purchase. Pair with target <c>offer_id</c> = a catalog offer id to require a
    /// specific offer ("buy offer 12"); leave the quest's target empty for "buy anything".</summary>
    public const string CatalogPurchase = "CatalogPurchase";

    /// <summary>The player changed their motto.</summary>
    public const string MottoChange = "MottoChange";

    /// <summary>The player completed a trade.</summary>
    public const string TradeCompleted = "TradeCompleted";

    /// <summary>The player created a group.</summary>
    public const string CreateGroup = "CreateGroup";

    /// <summary>The player joined a group.</summary>
    public const string JoinGroup = "JoinGroup";

    /// <summary>The player bought Membership.</summary>
    public const string BuyMembership = "BuyClub";

    /// <summary>The player placed furniture. Pair with target <c>base_item_id</c> = a furniture
    /// definition id to require a specific furni type; leave empty for "place anything".</summary>
    public const string PlaceItem = "PlaceItem";

    /// <summary>The conventional target-type key for <see cref="CatalogPurchase"/>: a catalog offer id.</summary>
    public const string TargetOfferId = "offer_id";

    /// <summary>The conventional target-type key for a furniture definition id (à la Arcturus).</summary>
    public const string TargetBaseItemId = "base_item_id";
}
