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

    // 2 was the targeted offer purchase, removed in M0. Persisted: never reuse it.

    MarketplaceList = 3,
    MarketplaceCancel = 4,
    MarketplaceBuy = 5,
    MarketplaceRedeem = 6,

    // 7 was RentableSpaceRent, removed in M0 bis with rentable spaces. Persisted: never reuse it.
    MembershipPurchase = 8,

    // 9 was the LTD raffle entry, removed in M0. Persisted: never reuse it.

    // 10 was RoomAdPurchase, removed in M0 with room advertisements. Persisted: never reuse it.

    DailyTaskReward = 11,

    // 12 was MintTokenPurchase, removed with collectibles in the RPG fork (M0). Never reuse it.

    /// <summary>A contract settled against a wired chest: coins and furniture, both directions.</summary>
    WiredChestContract = 13,

    /// <summary>A prize handed out for a furniture that was destroyed to get it — a crackable, a
    /// mystery trophy, a mystery box. The consumption is the pivot and it has already happened, so
    /// these operations are opened past it: they complete or they are owed.</summary>
    LootGrant = 14,

    /// <summary>What a completed quest pays. The row is marked complete first, so the reward is
    /// owed from before the payout runs.</summary>
    QuestReward = 15,

    /// <summary>What an achievement level pays: its badge, its currency and its score. Same shape —
    /// the level is persisted before any of it is handed over.</summary>
    AchievementReward = 16,

    // 17 (HabbiconPurchase), 18 (RewardTrackPremium) and 19 (RewardTrackPrize) were removed with
    // their features in the RPG fork (M0). The numbers are persisted: never reuse them.

    /// <summary>
    /// What a paid shop order hands over: credits, duckets, diamonds or club months, bought with
    /// real money. The only kind here whose pivot happens OUTSIDE the hotel — the payment provider
    /// captured the money before it sent the webhook — so these operations are opened already past
    /// it. Nothing refunds one: a grant that fails is owed, and the card was charged either way.
    /// </summary>
    ShopPurchase = 20,

    // 21 was NftClaim, removed with collectibles in the RPG fork (M0). Never reuse it.

    /// <summary>
    /// Cashing one Earnings row. The id is derived from the row, so a claim retried after a crash
    /// between the credit and the claimed mark finds its receipt and only marks.
    /// </summary>
    RewardVault = 22,

    /// <summary>
    /// An earning paid straight to the wallet — a currency the Earnings window cannot hold, or a
    /// category the operator routed direct. Derived from the player; the step is the source key.
    /// </summary>
    RewardVaultDirect = 23,
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

    /// <summary>Moving the staked furniture into the chest and the reward furniture out of it. The
    /// pivot of a chest contract: after it the goods have changed hands and the payment is no longer
    /// owed back.</summary>
    public const string CHEST_SWAP = "chest-swap";

    /// <summary>Paying the contract's reward coins out of the chest. Undoes itself into the chest
    /// when the wallet refuses, which is why the books are written after it and not before.</summary>
    public const string CHEST_PAYOUT = "chest-payout";

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
