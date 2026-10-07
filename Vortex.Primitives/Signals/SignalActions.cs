using System;
using System.Collections.Immutable;

namespace Vortex.Primitives.Signals;

/// <summary>
/// The vocabulary of things a player can be said to have done.
/// </summary>
/// <remarks>
/// <para>
/// This is the one place the strings are written. The last time this vocabulary was written down in
/// two places — the handlers and <c>RewardTrackActionFacts</c> — the two drifted on nine actions and
/// nobody noticed, because nothing compares two hand-kept lists.
/// </para>
/// <para>
/// The strings are persisted vocabulary: content in the database names them, so a value never
/// changes. They were the reward tracks' action codes (removed in M0), which were the target
/// client's artwork keys — <c>RewardTrackTaskRowView</c> built an icon name as
/// <c>"reward_track_tasks_" + actionType.toLowerCase()</c>; that is why most of them are spelled the
/// way they are.
/// </para>
/// <para>
/// The list is <em>declared</em>, not derived from the translators that exist. Two of these actions
/// have no producer today (<see cref="Teleport"/>, <see cref="Wired"/>) and content may already name
/// them; deriving the list would make them vanish from the editor rather than be shown as inert,
/// and a task written on one would become invisible instead of being flagged.
/// </para>
/// </remarks>
public static class SignalActions
{
    /// <summary>Entered another player's room. Distinct-mode tasks count distinct rooms. (Introduction Track)</summary>
    public const string EnterOtherUsersRoom = "enter_other_users_room";

    /// <summary>Created a room. (Introduction Track)</summary>
    public const string CreateRoom = "create_room";

    /// <summary>Placed furniture in a room. (Introduction Track)</summary>
    public const string PlaceItem = "place_item";

    /// <summary>Moved furniture already in a room. (Introduction Track)</summary>
    public const string MoveItem = "move_item";

    /// <summary>Rotated furniture already in a room. (Introduction Track)</summary>
    public const string RotateItem = "rotate_item";

    /// <summary>
    /// Took a placed piece of furniture back into the inventory. No client artwork -- there is no
    /// <c>reward_track_tasks_pick_up_item</c> embed -- so this is usable as a later step of a
    /// sequence, where nothing is drawn, but makes a poor step 0.
    /// </summary>
    public const string PickUpItem = "pick_up_item";

    /// <summary>
    /// Stepped onto a piece of floor furniture. No client artwork, same caveat as
    /// <see cref="PickUpItem"/>. Fires often, which is why nothing is published unless some
    /// content is actually listening for it.
    /// </summary>
    public const string WalkOnFurni = "walk_on_furni";

    /// <summary>Arrived somewhere through a teleport. (Introduction Track)</summary>
    public const string Teleport = "teleport";

    /// <summary>Said something a room accepted. (Introduction Track)</summary>
    public const string ChatWithSomeone = "chat_with_someone";

    /// <summary>Sent a friend request. (Introduction Track)</summary>
    public const string RequestFriend = "request_friend";

    /// <summary>Sent a private message. (Introduction Track)</summary>
    public const string SendMessengerMessage = "send_messenger_message";

    /// <summary>Danced in a room. (Introduction Track)</summary>
    public const string Dance = "dance";

    /// <summary>Waved in a room. (Introduction Track)</summary>
    public const string Wave = "wave";

    /// <summary>Held up a sign.</summary>
    public const string ShowSign = "show_sign";

    /// <summary>Changed the avatar's figure. (Introduction Track)</summary>
    public const string ChangeFigure = "change_figure";

    /// <summary>Changed the profile motto. (Introduction Track)</summary>
    public const string ChangeMotto = "change_motto";

    /// <summary>Equipped a badge. (Introduction Track)</summary>
    public const string WearBadge = "wear_badge";

    /// <summary>Bought from the catalog. (Introduction Track)</summary>
    public const string BuyFromCatalogue = "buy_from_catalogue";

    /// <summary>A pet gained a level. (Introduction Track)</summary>
    public const string PetLevel = "pet_level";

    /// <summary>Completed a trade. No client artwork.</summary>
    public const string CompleteTrade = "complete_trade";

    /// <summary>
    /// Spent currency. The amount is the signal's amount, so a task can require a total. No client
    /// artwork.
    /// </summary>
    public const string SpendCredits = "spend_credits";

    /// <summary>Completed a quest. No client artwork.</summary>
    public const string CompleteQuest = "complete_quest";

    /// <summary>Levelled up an achievement. No client artwork.</summary>
    public const string AchievementLevel = "achievement_level";

    /// <summary>
    /// Raised by the wired <c>PROGRESS_REWARD_TRACK</c> action, which names its track and task
    /// directly rather than going through an action code. Present so content and the dashboard can
    /// describe such a task; the engine routes wired progress by id, not by this code. No client
    /// artwork.
    /// </summary>
    public const string Wired = "wired";

    /// <summary>Left a room, with how long the stay lasted. (no Habbo artwork: this hotel's own signal)</summary>
    public const string LeaveRoom = "leave_room";

    /// <summary>Saved a room's settings, tags or category. (no Habbo artwork: this hotel's own signal)</summary>
    public const string UpdateRoomSettings = "update_room_settings";

    /// <summary>Answered the doorbell for someone. (no Habbo artwork: this hotel's own signal)</summary>
    public const string AnswerDoorbell = "answer_doorbell";

    /// <summary>Accepted a friend request. The other half of request_friend. (no Habbo artwork: this hotel's own signal)</summary>
    public const string AcceptFriend = "accept_friend";

    /// <summary>Logged in. (no Habbo artwork: this hotel's own signal)</summary>
    public const string Login = "login";

    /// <summary>Changed name. (no Habbo artwork: this hotel's own signal)</summary>
    public const string ChangeName = "change_name";

    /// <summary>Was granted a badge, however it arrived. (no Habbo artwork: this hotel's own signal)</summary>
    public const string EarnBadge = "earn_badge";

    /// <summary>Accepted a quest. (no Habbo artwork: this hotel's own signal)</summary>
    public const string AcceptQuest = "accept_quest";

    /// <summary>Activated an avatar effect. (no Habbo artwork: this hotel's own signal)</summary>
    public const string ActivateEffect = "activate_effect";

    /// <summary>Claimed a finished daily task. (no Habbo artwork: this hotel's own signal)</summary>
    public const string ClaimDailyTask = "claim_daily_task";

    /// <summary>Saved an outfit to the wardrobe. (no Habbo artwork: this hotel's own signal)</summary>
    public const string SaveOutfit = "save_outfit";

    /// <summary>Redeemed a clothing item. (no Habbo artwork: this hotel's own signal)</summary>
    public const string RedeemClothing = "redeem_clothing";

    /// <summary>Changed an account preference. (no Habbo artwork: this hotel's own signal)</summary>
    public const string ChangePreference = "change_preference";

    /// <summary>Founded a guild. (no Habbo artwork: this hotel's own signal)</summary>
    public const string CreateGroup = "create_group";

    /// <summary>Joined a guild. (no Habbo artwork: this hotel's own signal)</summary>
    public const string JoinGroup = "join_group";

    /// <summary>Made a guild the favourite one. (no Habbo artwork: this hotel's own signal)</summary>
    public const string FavouriteGroup = "favourite_group";

    /// <summary>Started a forum thread. (no Habbo artwork: this hotel's own signal)</summary>
    public const string CreateForumThread = "create_forum_thread";

    /// <summary>Posted on a forum thread. (no Habbo artwork: this hotel's own signal)</summary>
    public const string CreateForumPost = "create_forum_post";

    /// <summary>Adopted a pet. (no Habbo artwork: this hotel's own signal)</summary>
    public const string AdoptPet = "adopt_pet";

    /// <summary>Put a pet down in a room. (no Habbo artwork: this hotel's own signal)</summary>
    public const string PlacePet = "place_pet";

    /// <summary>Took a pet back out of a room. (no Habbo artwork: this hotel's own signal)</summary>
    public const string PickUpPet = "pick_up_pet";

    /// <summary>Put an item up for sale. (no Habbo artwork: this hotel's own signal)</summary>
    public const string ListOnMarketplace = "list_on_marketplace";

    /// <summary>Bought an item from the marketplace. (no Habbo artwork: this hotel's own signal)</summary>
    public const string BuyOnMarketplace = "buy_on_marketplace";

    /// <summary>Collected what the marketplace owed. (no Habbo artwork: this hotel's own signal)</summary>
    public const string RedeemMarketplaceCredits = "redeem_marketplace_credits";

    /// <summary>Bought or renewed a club membership. (no Habbo artwork: this hotel's own signal)</summary>
    public const string BuyClub = "buy_club";

    /// <summary>Claimed a club gift. (no Habbo artwork: this hotel's own signal)</summary>
    public const string ClaimClubGift = "claim_club_gift";

    /// <summary>Unwrapped a present. (no Habbo artwork: this hotel's own signal)</summary>
    public const string OpenPresent = "open_present";

    /// <summary>Bought something for somebody else. (no Habbo artwork: this hotel's own signal)</summary>
    public const string BuyGift = "buy_gift";

    /// <summary>Claimed vault income. (no Habbo artwork: this hotel's own signal)</summary>
    public const string ClaimVaultIncome = "claim_vault_income";

    /// <summary>A pet obeyed an order. Raised where it obeys, never where the words were typed.</summary>
    public const string CommandPet = "command_pet";

    /// <summary>
    /// Stood on a tile beside a piece of furniture, or on it. Carries which side, measured from the
    /// furniture, so "in front of the television" survives the television being turned round.
    /// </summary>
    public const string StandNearFurni = "stand_near_furni";

    /// <summary>Was given something bought from the catalogue. The other half of buy_gift.</summary>
    public const string ReceiveGift = "receive_gift";

    /// <summary>
    /// Every core action. The governance test freezes this list, and the translator processor uses
    /// it to tell a core action from one a plugin must qualify with its own key.
    /// </summary>
    public static readonly ImmutableHashSet<string> All = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        EnterOtherUsersRoom,
        CreateRoom,
        PlaceItem,
        MoveItem,
        RotateItem,
        PickUpItem,
        WalkOnFurni,
        Teleport,
        ChatWithSomeone,
        RequestFriend,
        SendMessengerMessage,
        Dance,
        Wave,
        ShowSign,
        ChangeFigure,
        ChangeMotto,
        WearBadge,
        BuyFromCatalogue,
        PetLevel,
        CompleteTrade,
        SpendCredits,
        CompleteQuest,
        AchievementLevel,
        Wired,
        LeaveRoom,
        UpdateRoomSettings,
        AnswerDoorbell,
        AcceptFriend,
        Login,
        ChangeName,
        EarnBadge,
        AcceptQuest,
        ActivateEffect,
        ClaimDailyTask,
        SaveOutfit,
        RedeemClothing,
        ChangePreference,
        CreateGroup,
        JoinGroup,
        FavouriteGroup,
        CreateForumThread,
        CreateForumPost,
        AdoptPet,
        PlacePet,
        PickUpPet,
        ListOnMarketplace,
        BuyOnMarketplace,
        RedeemMarketplaceCredits,
        BuyClub,
        ClaimClubGift,
        OpenPresent,
        BuyGift,
        ClaimVaultIncome,
        CommandPet,
        StandNearFurni,
        ReceiveGift
    );
}
