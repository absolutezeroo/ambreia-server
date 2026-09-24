using System;
using Orleans;

namespace Vortex.Primitives.Commerce;

/// <summary>
/// The identity of one value-moving operation, from the moment it is prepared until it completes or
/// is escalated. Every flow that debits a wallet mints one.
/// <para>
/// It exists because Orleans is at-most-once by default and does not deduplicate durably in the
/// presence of retries: without an identity on the operation, a replayed refund credits twice and a
/// crash between a durable debit and its delivery leaves nothing to resume from.
/// </para>
/// </summary>
/// <remarks>
/// A version 7 GUID rather than a ULID library: it is time-ordered the same way, so the journal's
/// primary key stays insert-friendly, and it costs no dependency.
/// </remarks>
[GenerateSerializer, Immutable]
public readonly record struct CommerceOperationId(Guid Value)
{
    public static CommerceOperationId New() => new(Guid.CreateVersion7());

    /// <summary>
    /// The id of the operation that acts on a given entity in a given flow — the same id every time.
    /// </summary>
    /// <remarks>
    /// Some flows have no separate retry mechanism: their retry is the player clicking the button
    /// again. Cancelling a marketplace offer is one. A fresh id each time would make the second click
    /// a second operation and hand the item back twice; deriving the id from the offer makes it the
    /// same operation asking again, which the receipts already know how to answer.
    /// </remarks>
    public static CommerceOperationId Deterministic(CommerceOperationKind kind, int entityId)
    {
        Span<byte> bytes = stackalloc byte[16];

        // A fixed marker in the first four bytes keeps these from colliding with a version 7 GUID,
        // whose leading bytes are a timestamp.
        bytes[0] = 0xC0;
        bytes[1] = 0x4E;
        bytes[2] = (byte)kind;
        bytes[3] = 0x00;

        BitConverter.TryWriteBytes(bytes[4..], entityId);

        return new CommerceOperationId(new Guid(bytes));
    }

    public static readonly CommerceOperationId None = new(Guid.Empty);

    public bool IsNone => Value == Guid.Empty;

    public override string ToString() => Value.ToString("N");
}

/// <summary>
/// Where an operation is. The pivot is the one transition that matters: before it, failure is
/// compensated and the player is made whole; after it, failure is retried until it succeeds or an
/// operator is called. There is no state that means "refund an operation that already pivoted".
/// </summary>
public enum CommerceOperationState
{
    /// <summary>Preflight passed. Nothing durable has happened yet, and nothing has to be undone.</summary>
    Prepared = 0,

    /// <summary>The wallet debit is committed. Compensation is still the correct response to a failure.</summary>
    Debited = 1,

    /// <summary>
    /// The pivot is past. The operation is now owed to the player, and the only ways out are
    /// completion or intervention — never a refund.
    /// </summary>
    Pivoted = 2,

    /// <summary>Post-pivot steps are running or being retried.</summary>
    Completing = 3,

    /// <summary>Every step is done.</summary>
    Completed = 4,

    /// <summary>Failed while compensation was still correct, and was compensated.</summary>
    FailedBeforePivot = 5,

    /// <summary>
    /// Retries are exhausted after the pivot. An operator has to look at it. Nothing is ever
    /// compensated from here — an invented compensation after the pivot is how one bug becomes two.
    /// </summary>
    NeedsIntervention = 6,
}

/// <summary>Which flow an operation belongs to. Bounded, so it is safe as a metric tag.</summary>
public enum CommerceOperationKind
{
    CatalogPurchase = 0,
    Gift = 1,
    TargetedOffer = 2,
    MarketplaceList = 3,
    MarketplaceCancel = 4,
    MarketplaceBuy = 5,
    MarketplaceRedeem = 6,
    RentableSpaceRent = 7,
    ClubPurchase = 8,
    LtdRaffleEntry = 9,
    RoomAdPurchase = 10,
    DailyTaskReward = 11,
    MintTokenPurchase = 12,

    /// <summary>A contract settled against a wired chest: coins and furniture, both directions.</summary>
    WiredChestContract = 13,

    /// <summary>A prize handed out for a furniture that was destroyed to get it — a crackable, a
    /// mystery trophy, a mystery box. The consumption is the pivot and it has already happened, so
    /// these operations are opened past it: they complete or they are owed.</summary>
    PrizeGrant = 14,

    /// <summary>What a completed quest pays. The row is marked complete first, so the reward is
    /// owed from before the payout runs.</summary>
    QuestReward = 15,

    /// <summary>What an achievement level pays: its badge, its currency and its score. Same shape —
    /// the level is persisted before any of it is handed over.</summary>
    AchievementReward = 16,

    /// <summary>Buying a Habbicon, singly or as a whole collection.</summary>
    HabbiconPurchase = 17,

    /// <summary>Buying premium on one reward track. May cost credits and diamonds together.</summary>
    RewardTrackPremium = 18,

    /// <summary>
    /// What a claimed reward-track prize hands over. The claim row is written first, so the bundle
    /// is owed from before any of it is granted.
    /// </summary>
    RewardTrackPrize = 19,

    /// <summary>
    /// What a paid shop order hands over: credits, duckets, diamonds or club months, bought with
    /// real money. The only kind here whose pivot happens OUTSIDE the hotel — the payment provider
    /// captured the money before it sent the webhook — so these operations are opened already past
    /// it. Nothing refunds one: a grant that fails is owed, and the card was charged either way.
    /// </summary>
    ShopPurchase = 20,

    /// <summary>
    /// The furniture a collectible entitlement hands over. Its own kind rather than a borrowed one
    /// because the id is derived from the claim row, and two flows sharing a kind would collide the
    /// moment their entity ids met.
    /// </summary>
    NftClaim = 21,

    /// <summary>
    /// Cashing one Earnings row. The id is derived from the row, so a claim retried after a crash
    /// between the credit and the claimed mark finds its receipt and only marks.
    /// </summary>
    VaultIncome = 22,

    /// <summary>
    /// An earning paid straight to the wallet — a currency the Earnings window cannot hold, or a
    /// category the operator routed direct. Derived from the player; the step is the source key.
    /// </summary>
    VaultIncomeDirect = 23,
}

/// <summary>
/// The step keys a receipt is written under. A step key is scoped to its operation, so the same name
/// may mean different work in two flows; what matters is that one operation applies one step once.
/// </summary>
public static class CommerceStepKeys
{
    /// <summary>The wallet debit that funds the operation.</summary>
    public const string DEBIT = "debit";

    /// <summary>The compensating credit, before the pivot only.</summary>
    public const string REFUND = "refund";

    /// <summary>The local inventory batch: furniture, badges, pets and bots in one commit.</summary>
    public const string LOCAL_GRANT = "local-grant";

    /// <summary>One avatar effect. Suffixed by index, because an offer may grant several.</summary>
    public const string EFFECT = "effect";

    /// <summary>Wrapping a purchased offer as a present for the recipient.</summary>
    public const string GIFT_WRAP = "gift-wrap";

    /// <summary>The targeted offer's per-player purchase counter.</summary>
    public const string TARGETED_COUNT = "targeted-count";

    /// <summary>Handing the bought item to the buyer, after the marketplace claim.</summary>
    public const string MARKETPLACE_DELIVER = "marketplace-deliver";

    /// <summary>Returning a cancelled offer's item to its seller.</summary>
    public const string MARKETPLACE_RESTORE = "marketplace-restore";

    /// <summary>Paying a seller what their sold offers owe them.</summary>
    public const string MARKETPLACE_CREDIT = "marketplace-credit";

    /// <summary>Removing the listed item from the seller's inventory.</summary>
    public const string MARKETPLACE_WITHDRAW = "marketplace-withdraw";

    /// <summary>Crediting the stamps a mint-token purchase bought.</summary>
    public const string MINT_TOKENS = "mint-tokens";

    /// <summary>Paying out what a completed task promised.</summary>
    public const string REWARD_PAYOUT = "reward-payout";

    /// <summary>The advertisement row that puts a room in front of the navigator.</summary>
    public const string ROOM_AD = "room-ad";

    /// <summary>The club months, their badges and the events that go with them.</summary>
    public const string CLUB_MONTHS = "club-months";

    /// <summary>The raffle entry row that makes a paid ticket a ticket.</summary>
    public const string LTD_ENTRY = "ltd-entry";

    /// <summary>Writing the rental itself: who holds the space and until when.</summary>
    public const string RENTABLE_SPACE_GRANT = "rentable-space-grant";

    /// <summary>Paying the space's owner what their tenant just paid. Strictly after the pivot: the
    /// tenant already holds the space, so a failure here is owed, never refunded.</summary>
    public const string RENTABLE_SPACE_OWNER_CREDIT = "rentable-space-owner-credit";

    /// <summary>Moving the staked furniture into the chest and the reward furniture out of it. The
    /// pivot of a chest contract: after it the goods have changed hands and the payment is no longer
    /// owed back.</summary>
    public const string CHEST_SWAP = "chest-swap";

    /// <summary>Paying the contract's reward coins out of the chest. Undoes itself into the chest
    /// when the wallet refuses, which is why the books are written after it and not before.</summary>
    public const string CHEST_PAYOUT = "chest-payout";

    /// <summary>Writing the Habbicon ownership rows a purchase bought.</summary>
    public const string HABBICON_GRANT = "habbicon-grant";

    /// <summary>Turning premium on for one reward track, and crediting its instant points.</summary>
    public const string REWARD_TRACK_PREMIUM = "reward-track-premium";

    /// <summary>
    /// One reward of a claimed prize's bundle, suffixed by index. Strictly after the pivot — the
    /// claim row is written first, so a reward that fails here is owed rather than rolled back, and
    /// an operator can see exactly which one.
    /// </summary>
    public const string REWARD_TRACK_GRANT = "reward-track-grant";

    /// <summary>
    /// What a paid shop order hands over. Strictly after the pivot — the money was captured by the
    /// payment provider before the hotel heard about it — so this step is retried until it lands or
    /// an operator is called, and is never rolled back.
    /// </summary>
    public const string SHOP_GRANT = "shop-grant";

    /// <summary>The critical business event the journal relays once the operation is terminal.</summary>
    public const string RELAY = "relay";

    /// <summary>Paying one claimed Earnings row.</summary>
    public const string VAULT_CLAIM = "vault-claim";

    /// <summary>One step of an operation that repeats, e.g. the nth effect of a bundle.</summary>
    public static string Indexed(string step, int index) => $"{step}:{index}";
}
