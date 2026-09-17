namespace Vortex.Primitives.RewardTracks;

/// <summary>
/// How a task's stored progress relates to the events that feed it. The mode is content, not code:
/// adding a task never adds a class, it picks one of these.
/// </summary>
public enum TaskProgressMode
{
    /// <summary>Every matching signal adds its amount. "Send 50 messages", "spend 100 credits".</summary>
    Counter = 0,

    /// <summary>
    /// Only signals carrying a key not seen before count, one each. "Visit 20 different rooms".
    /// The seen keys are kept on the progress row and stop being recorded once the task's highest
    /// requirement is met, so the set is bounded by the content rather than by the player.
    /// </summary>
    Distinct = 1,

    /// <summary>
    /// The signal reports a total rather than an increment; progress becomes that total. "Have 5
    /// friends", "own 10 furni" — a state the world can also take away.
    /// </summary>
    Absolute = 2,

    /// <summary>
    /// As <see cref="Absolute"/>, but progress never goes down. For a high-water mark the player
    /// should not lose ("reach level 10 with a pet").
    /// </summary>
    Highest = 3,
}

/// <summary>Where a reward track is in its editing/publishing lifecycle.</summary>
/// <remarks>
/// Only <see cref="Active"/> and <see cref="Ended"/> are visible to players — <see cref="Ended"/>
/// so a track whose progress window closed can still be claimed from. Everything else is an
/// operator-side state, which is what lets a track be built without leaking half of it to the
/// hotel.
/// </remarks>
public enum RewardTrackStatus
{
    /// <summary>Being written. Never served, never progresses, freely editable.</summary>
    Draft = 0,

    /// <summary>Finished and published, but <c>StartsAt</c> has not arrived.</summary>
    Scheduled = 1,

    /// <summary>Live.</summary>
    Active = 2,

    /// <summary>Past its progress window. Still served while claims remain open.</summary>
    Ended = 3,

    /// <summary>Retired. Never served; player rows are kept for history.</summary>
    Archived = 4,
}

/// <summary>What has to be true before a player sees a track at all.</summary>
public enum RewardTrackUnlockKind
{
    /// <summary>No condition.</summary>
    Always = 0,

    /// <summary>Another track must be complete. <c>UnlockValue</c> is that track's id.</summary>
    TrackCompleted = 1,

    /// <summary>A specific prize of another track must be claimed. <c>UnlockValue</c> is <c>trackId:prizeId</c>.</summary>
    PrizeClaimed = 2,

    /// <summary>The player must hold a badge. <c>UnlockValue</c> is the badge code.</summary>
    BadgeOwned = 3,

    /// <summary>The account must be at least this many days old. <c>UnlockValue</c> parses as an int.</summary>
    AccountAgeDays = 4,

    /// <summary>A server-config flag must be true. <c>UnlockValue</c> is the config key.</summary>
    FeatureFlag = 5,
}

/// <summary>
/// What a reward hands over. The numeric values are the client's own product-type ids, read from
/// <c>ProductIconWidget.previewImage</c>, which switches on <c>productTypeId</c> to decide what the
/// accompanying <c>rewardTypeId</c> string means. Keeping our enum on the client's numbering means
/// the serializer writes the field straight out with no translation table to drift.
/// </summary>
public enum RewardKind
{
    /// <summary>Nothing. The client draws its "unknown product" tile.</summary>
    None = -1,

    /// <summary>A wall item. <c>RewardTypeId</c> is the wall item type id.</summary>
    WallItem = 0,

    /// <summary>A floor item. <c>RewardTypeId</c> is the furniture definition id.</summary>
    FloorItem = 1,

    /// <summary>An avatar effect. <c>RewardTypeId</c> is the effect id.</summary>
    AvatarEffect = 2,

    /// <summary>A badge. <c>RewardTypeId</c> is the badge code.</summary>
    Badge = 4,

    /// <summary>A bot. <c>RewardTypeId</c> names the bot; <c>ExtraParams</c> carries its figure.</summary>
    Bot = 6,

    /// <summary>
    /// Currency. <c>RewardTypeId</c> is the activity-point type (-1 credits, 0 duckets, 5 diamonds),
    /// which is exactly what the client feeds to its purse icon lookup.
    /// </summary>
    Currency = 8,

    /// <summary>A chat style. <c>RewardTypeId</c> is the style id.</summary>
    ChatStyle = 9,

    /// <summary>A pet. <c>RewardTypeId</c> is the pet type; <c>ExtraParams</c> carries its figure.</summary>
    Pet = 10,

    /// <summary>A Habbicon. <c>RewardTypeId</c> is the Habbicon id.</summary>
    Habbicon = 12,

    /// <summary>
    /// A named entitlement (<c>trading_pass</c>, a feature unlock, …). <c>RewardTypeId</c> is the
    /// entitlement key. Outside the client's own vocabulary on purpose: it renders as the unknown
    /// tile, and the reward is still granted. Use it for anything that is a permission, not a thing.
    /// </summary>
    Entitlement = 100,

    /// <summary>
    /// VortexCoin, minted to the player's custodial address. <c>Amount</c> is WHOLE coins — the
    /// contract's eighteen decimals are applied by <c>IChainRewardService</c> and appear nowhere in
    /// content. <c>RewardTypeId</c> is unused.
    /// </summary>
    /// <remarks>
    /// Outside the client's numbering for the same reason <see cref="Entitlement" /> is: it has no
    /// product type for a coin that lives on a chain, so it draws the unknown tile and the reward
    /// is still granted. Unlike every other kind, this one costs an Ethereum transaction to hand
    /// over — seconds, and fallible for reasons that pass — so whatever grants it must be somewhere
    /// that can wait, and must treat a failure as owed rather than refused.
    /// </remarks>
    Coin = 101,
}

/// <summary>
/// Why a prize claim was refused. The numbers are the client's: it looks up
/// <c>reward_track.claim.notification.fail.&lt;code&gt;</c> and shows the localized line, so these
/// must not be renumbered.
/// </summary>
public enum RewardClaimResult
{
    Success = 0,

    /// <summary>"Reward tracks are currently disabled"</summary>
    Disabled = 1,

    /// <summary>"Reward track not found"</summary>
    TrackNotFound = 2,

    /// <summary>"Reward not found"</summary>
    RewardNotFound = 3,

    /// <summary>"You are not eligible for this reward"</summary>
    NotEligible = 4,

    /// <summary>"You do not have enough points for this reward"</summary>
    NotEnoughPoints = 5,

    /// <summary>"This reward was already claimed"</summary>
    AlreadyClaimed = 6,

    /// <summary>"Failed to claim reward"</summary>
    GrantFailed = 7,

    /// <summary>"Premium is required for this reward"</summary>
    PremiumRequired = 8,
}

/// <summary>
/// Why a premium purchase was refused, in the client's numbering
/// (<c>reward_track.premium.notification.fail.&lt;code&gt;</c>).
/// </summary>
public enum RewardPremiumResult
{
    Success = 0,

    /// <summary>"Reward tracks are currently disabled"</summary>
    Disabled = 1,

    /// <summary>"Reward track not found"</summary>
    TrackNotFound = 2,

    /// <summary>"You are not eligible for premium on this track"</summary>
    NotEligible = 3,

    /// <summary>"Premium is not configured for this track"</summary>
    NotConfigured = 4,

    /// <summary>"You already own premium for this track"</summary>
    AlreadyOwned = 5,

    /// <summary>"Premium could not be purchased because the configuration is invalid"</summary>
    InvalidConfiguration = 6,

    /// <summary>"You do not have enough credits"</summary>
    NotEnoughCredits = 7,

    /// <summary>"You do not have enough diamonds"</summary>
    NotEnoughDiamonds = 8,

    /// <summary>"Failed to unlock premium track"</summary>
    Failed = 9,
}

/// <summary>What makes a track "complete" for the <c>RewardTrackCompleted</c> transition.</summary>
/// <remarks>
/// Distinct from the two booleans on the wire: the client computes <c>complete</c> as "every free
/// prize claimed" and <c>premiumComplete</c> as "every prize claimed" itself, and those are display
/// state. This is the server's own notion, the one a follow-on track unlocks from.
/// </remarks>
public enum RewardTrackCompletionPolicy
{
    /// <summary>Every free prize has been claimed. Matches what the client shows as complete.</summary>
    AllFreePrizesClaimed = 0,

    /// <summary>Every prize, free and premium, has been claimed.</summary>
    AllPrizesClaimed = 1,

    /// <summary>Points reached the highest prize's requirement, claimed or not.</summary>
    MaxPointsReached = 2,

    /// <summary>Every task hit its last stage.</summary>
    AllTasksCompleted = 3,
}

/// <summary>How a step filter compares a fact to its value.</summary>
public enum StepFilterOperator
{
    /// <summary>Exact match.</summary>
    Equals = 0,

    /// <summary>Anything but this. "Any room except the one you started in".</summary>
    NotEquals = 1,

    /// <summary>The value is a comma-separated list and the fact must be one of it.</summary>
    OneOf = 2,

    /// <summary>
    /// The fact contains this text, ignoring case. The only operator that is any use on a name or a
    /// description: an exact match on free text an operator never typed is a filter that never
    /// fires, and "a flat with 'casino' in the name" is the task somebody actually writes.
    /// </summary>
    Contains = 3,
}
