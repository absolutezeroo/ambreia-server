namespace Vortex.Primitives.RewardTracks;

/// <summary>
/// The action codes a reward-track task can be defined on. A task names one of these in its
/// <c>action_code</c> column; the engine's event bridge raises the matching code when the
/// corresponding domain action has actually succeeded.
/// </summary>
/// <remarks>
/// <para>
/// These are content identifiers, not behaviour: nothing here is a class, a switch arm or a
/// handler. Adding a task to a campaign picks a code; adding a <em>new</em> code means there is a
/// new gameplay signal to bridge, and only then is there code to write — one event handler in
/// <c>RewardTrackEventHandlers</c>.
/// </para>
/// <para>
/// THE STRING IS THE CLIENT'S ARTWORK KEY, AND THE CLIENT SETTLES IT. <c>RewardTrackTaskRowView</c>
/// builds the icon name as <c>"reward_track_tasks_" + actionType.toLowerCase()</c> and nothing else,
/// so a code that is not one of Habbo's own leaves the task with a blank square and a
/// <c>ResourceManager: Asset not found</c> warning. The vocabulary is therefore fixed by the thirty
/// <c>reward_track_tasks_*</c> embeds declared in the client's <c>HabboWindowManagerCom.as</c> — not
/// by <c>external_flash_texts</c>, which an earlier version of this file cited. That mistake is why
/// twelve of these codes used to be invented names (<c>visit_rooms</c>, <c>chat_with_users</c>,
/// <c>place_furniture</c>, …) and every task in the Introduction Track rendered without an icon.
/// </para>
/// <para>
/// <c>external_flash_texts</c> governs a different string: a task's <c>task_id</c>, which becomes
/// the localization stem <c>reward_track.&lt;track&gt;.task.&lt;task_id&gt;.name</c>. The two spaces
/// do not line up — the Introduction Track's <c>visit_rooms</c> task is driven by the
/// <c>enter_other_users_room</c> action — and a seed must not reuse one for the other.
/// </para>
/// <para>
/// The last six codes have no Habbo artwork at all: they are this hotel's own signals, and a task
/// defined on one shows an empty icon. That is deliberate — inventing a name would only move the
/// blank square, and borrowing an unrelated icon would lie about what the task is.
/// </para>
/// </remarks>
public static class RewardTrackActions
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

    /// <summary>Gave respect to another player. (Introduction Track)</summary>
    public const string GiveRespect = "give_respect";

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

    /// <summary>
    /// Rated a room, up or down. No Habbo artwork exists for it, like the codes above it: this
    /// hotel's own signal, and a task defined on it shows an empty icon rather than a borrowed one
    /// that would lie about what it is.
    /// </summary>
    public const string RateRoom = "rate_room";

    /// <summary>Left a room, with how long the stay lasted. (no Habbo artwork: this hotel's own signal)</summary>
    public const string LeaveRoom = "leave_room";

    /// <summary>Saved a room's settings, tags or category. (no Habbo artwork: this hotel's own signal)</summary>
    public const string UpdateRoomSettings = "update_room_settings";

    /// <summary>Answered the doorbell for someone. (no Habbo artwork: this hotel's own signal)</summary>
    public const string AnswerDoorbell = "answer_doorbell";

    /// <summary>Accepted a friend request. The other half of request_friend. (no Habbo artwork: this hotel's own signal)</summary>
    public const string AcceptFriend = "accept_friend";

    /// <summary>Was given respect by somebody else. (no Habbo artwork: this hotel's own signal)</summary>
    public const string ReceiveRespect = "receive_respect";

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

    /// <summary>Finished a poll. (no Habbo artwork: this hotel's own signal)</summary>
    public const string CompletePoll = "complete_poll";

    /// <summary>Submitted a quiz. (no Habbo artwork: this hotel's own signal)</summary>
    public const string SubmitQuiz = "submit_quiz";

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

    /// <summary>Opened a mystery box. (no Habbo artwork: this hotel's own signal)</summary>
    public const string OpenMysteryBox = "open_mystery_box";

    /// <summary>Opened a mystery trophy. (no Habbo artwork: this hotel's own signal)</summary>
    public const string OpenMysteryTrophy = "open_mystery_trophy";

    /// <summary>Bought something for somebody else. (no Habbo artwork: this hotel's own signal)</summary>
    public const string BuyGift = "buy_gift";

    /// <summary>Bought a targeted offer. (no Habbo artwork: this hotel's own signal)</summary>
    public const string BuyTargetedOffer = "buy_targeted_offer";

    /// <summary>Entered an LTD raffle. (no Habbo artwork: this hotel's own signal)</summary>
    public const string EnterRaffle = "enter_raffle";

    /// <summary>Won an LTD raffle. (no Habbo artwork: this hotel's own signal)</summary>
    public const string WinRaffle = "win_raffle";

    /// <summary>Redeemed a voucher. (no Habbo artwork: this hotel's own signal)</summary>
    public const string RedeemVoucher = "redeem_voucher";

    /// <summary>Claimed vault income. (no Habbo artwork: this hotel's own signal)</summary>
    public const string ClaimVaultIncome = "claim_vault_income";

    /// <summary>Scratched somebody's pet.</summary>
    public const string RespectPet = "respect_pet";

    /// <summary>Had one of your own pets scratched. The other half of respect_pet.</summary>
    public const string PetRespected = "pet_respected";

    /// <summary>Fed a pet by hand. Carries the food's nutrition as its amount.</summary>
    public const string FeedPet = "feed_pet";

    /// <summary>A pet obeyed an order. Raised where it obeys, never where the words were typed.</summary>
    public const string CommandPet = "command_pet";

    /// <summary>
    /// Stood on a tile beside a piece of furniture, or on it. Carries which side, measured from the
    /// furniture, so "in front of the television" survives the television being turned round.
    /// </summary>
    public const string StandNearFurni = "stand_near_furni";

    /// <summary>Was given something bought from the catalogue. The other half of buy_gift.</summary>
    public const string ReceiveGift = "receive_gift";
}
