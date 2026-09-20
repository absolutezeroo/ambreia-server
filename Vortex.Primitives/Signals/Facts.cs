using System;
using System.Collections.Immutable;
using System.Linq;
using Vortex.Primitives.MysteryBox;
using Vortex.Primitives.RewardTracks;
using Vortex.Primitives.Rooms.Enums;

namespace Vortex.Primitives.Signals;

/// <summary>
/// The core fact vocabulary, typed.
/// </summary>
/// <remarks>
/// <para>
/// Every key forwards to <see cref="RewardTrackFacts"/> for the same reason
/// <see cref="SignalActions"/> forwards to <c>RewardTrackActions</c>: the strings are in the
/// database, and a second hand-written copy is a drift waiting to happen. What is new here is the
/// <see cref="FactKind"/> beside each one — the thing the dashboard used to hardcode per page.
/// </para>
/// <para>
/// <see cref="RoomOwner"/> is deliberately absent from the list an editor sees. Nothing emits it:
/// <c>PlayerEnteredRoomEvent</c> does not carry the owner, and reading it per entry would be a grain
/// call on an arrival path that has been slow before. It stays declared in
/// <see cref="RewardTrackFacts"/> so the shape is obvious to whoever plumbs it, and out of here so
/// nobody can pick a filter that never matches.
/// </para>
/// </remarks>
public static class Facts
{
    /// <summary>
    /// What the signal is mainly about. Never declared by a translator: the host republishes
    /// <see cref="ProgressSignal.Target"/> under this key, and a shape says what it means for that
    /// action through <see cref="SignalShape.TargetKind"/>.
    /// </summary>
    public const string TargetKey = RewardTrackFacts.Target;

    /// <summary>The room object id of a piece of furniture: the identity that survives a move.</summary>
    public static readonly FactKey Item = new(
        RewardTrackFacts.Item,
        FactKind.OpaqueId,
        "rewardTracks.fact_item",
        "The furniture"
    );

    /// <summary>A furniture definition id — the kind of thing, not the thing.</summary>
    public static readonly FactKey Definition = new(
        RewardTrackFacts.Definition,
        FactKind.FurnitureId,
        "rewardTracks.fact_def",
        "Furniture type"
    );

    /// <summary>Floor or wall: the coarse type no definition id can express on its own.</summary>
    public static readonly FactKey Placement = new(
        RewardTrackFacts.Placement,
        FactKind.Enum,
        "rewardTracks.fact_kind",
        "Floor or wall",
        [
            new(RewardTrackFacts.PlacementFloor, "rewardTracks.placement_floor", "Floor item"),
            new(RewardTrackFacts.PlacementWall, "rewardTracks.placement_wall", "Wall item"),
        ]
    );

    /// <summary>A room id.</summary>
    public static readonly FactKey Room = new(
        RewardTrackFacts.Room,
        FactKind.RoomId,
        "rewardTracks.fact_room",
        "Room"
    );

    /// <summary>A room's name. Worth filtering only with <c>Contains</c>.</summary>
    public static readonly FactKey RoomName = new(
        RewardTrackFacts.RoomName,
        FactKind.Text,
        "rewardTracks.fact_name",
        "Room name"
    );

    /// <summary>A room's description.</summary>
    public static readonly FactKey RoomDescription = new(
        RewardTrackFacts.RoomDescription,
        FactKind.Text,
        "rewardTracks.fact_desc",
        "Room description"
    );

    /// <summary>A navigator flat-category id.</summary>
    public static readonly FactKey Category = new(
        RewardTrackFacts.Category,
        FactKind.CategoryId,
        "rewardTracks.fact_category",
        "Category"
    );

    /// <summary>A room model name — the layout.</summary>
    public static readonly FactKey Model = new(
        RewardTrackFacts.Model,
        FactKind.Text,
        "rewardTracks.fact_model",
        "Room model"
    );

    /// <summary>Another player's id: the one respected, befriended, traded with.</summary>
    public static readonly FactKey Player = new(
        RewardTrackFacts.Player,
        FactKind.PlayerId,
        "rewardTracks.fact_player",
        "The other player"
    );

    /// <summary>A catalogue offer id.</summary>
    public static readonly FactKey Offer = new(
        RewardTrackFacts.Offer,
        FactKind.OfferId,
        "rewardTracks.fact_offer",
        "Catalogue offer"
    );

    /// <summary>A Habbicon id.</summary>
    public static readonly FactKey Habbicon = new(
        RewardTrackFacts.Habbicon,
        FactKind.OpaqueId,
        "rewardTracks.fact_habbicon",
        "Habbicon"
    );

    /// <summary>A Habbicon collection code.</summary>
    public static readonly FactKey Collection = new(
        RewardTrackFacts.Collection,
        FactKind.Text,
        "rewardTracks.fact_collection",
        "Collection"
    );

    /// <summary>A pet id.</summary>
    public static readonly FactKey Pet = new(
        RewardTrackFacts.Pet,
        FactKind.OpaqueId,
        "rewardTracks.fact_pet",
        "Pet"
    );

    /// <summary>A badge code.</summary>
    public static readonly FactKey Badge = new(
        RewardTrackFacts.Badge,
        FactKind.BadgeCode,
        "rewardTracks.fact_badge",
        "Badge"
    );

    /// <summary>
    /// What a chat line said.
    /// </summary>
    /// <remarks>
    /// The one fact that is only useful with <c>Contains</c>. An exact match on a line a player
    /// typed is a filter that never fires, which is why the operator existed before anything could
    /// use it.
    /// </remarks>
    public static readonly FactKey ChatMessage = new(
        RewardTrackFacts.ChatMessage,
        FactKind.Text,
        "rewardTracks.fact_message",
        "What was said"
    );

    /// <summary>Which way a room rating went: <c>1</c> for a like, <c>-1</c> for a dislike.</summary>
    public static readonly FactKey RatingPoints = new(
        RewardTrackFacts.RatingPoints,
        FactKind.Number,
        "rewardTracks.fact_points",
        "Rating"
    );

    /// <summary>A guild id.</summary>
    public static readonly FactKey Group = new(
        RewardTrackFacts.Group,
        FactKind.OpaqueId,
        "rewardTracks.fact_group",
        "Guild"
    );

    /// <summary>A forum thread id.</summary>
    public static readonly FactKey Thread = new(
        RewardTrackFacts.Thread,
        FactKind.OpaqueId,
        "rewardTracks.fact_thread",
        "Forum thread"
    );

    /// <summary>What it cost.</summary>
    public static readonly FactKey Price = new(
        RewardTrackFacts.Price,
        FactKind.Number,
        "rewardTracks.fact_price",
        "Price"
    );

    /// <summary>How many.</summary>
    public static readonly FactKey Quantity = new(
        RewardTrackFacts.Quantity,
        FactKind.Number,
        "rewardTracks.fact_quantity",
        "Quantity"
    );

    /// <summary>A poll's code. Picked, never typed: a poll nobody ran is a filter nobody satisfies.</summary>
    public static readonly FactKey Poll = new(
        RewardTrackFacts.Poll,
        FactKind.Text,
        "rewardTracks.fact_poll",
        "Poll"
    );

    /// <summary>A quiz's code.</summary>
    public static readonly FactKey Quiz = new(
        RewardTrackFacts.Quiz,
        FactKind.Text,
        "rewardTracks.fact_quiz",
        "Quiz"
    );

    /// <summary>A quest campaign's code.</summary>
    public static readonly FactKey Campaign = new(
        RewardTrackFacts.Campaign,
        FactKind.Text,
        "rewardTracks.fact_campaign",
        "Campaign"
    );

    /// <summary>A voucher's code.</summary>
    public static readonly FactKey Voucher = new(
        RewardTrackFacts.Voucher,
        FactKind.Text,
        "rewardTracks.fact_voucher",
        "Voucher"
    );

    /// <summary>A club gift's product code.</summary>
    public static readonly FactKey ClubGift = new(
        RewardTrackFacts.ClubGift,
        FactKind.Text,
        "rewardTracks.fact_club_gift",
        "Club gift"
    );

    /// <summary>A collectibles-store product code.</summary>
    public static readonly FactKey NftProduct = new(
        RewardTrackFacts.NftProduct,
        FactKind.Text,
        "rewardTracks.fact_nft_product",
        "Store product"
    );

    /// <summary>A targeted offer's identifier.</summary>
    public static readonly FactKey TargetedOffer = new(
        RewardTrackFacts.TargetedOffer,
        FactKind.Text,
        "rewardTracks.fact_targeted_offer",
        "Targeted offer"
    );

    /// <summary>A vault income category.</summary>
    public static readonly FactKey VaultCategory = new(
        RewardTrackFacts.VaultCategory,
        FactKind.Text,
        "rewardTracks.fact_vault_category",
        "Vault category"
    );

    /// <summary>A pet's species.</summary>
    public static readonly FactKey PetType = new(
        RewardTrackFacts.PetType,
        FactKind.Number,
        "rewardTracks.fact_pet_type",
        "Pet species"
    );

    /// <summary>
    /// An order a pet was given.
    /// </summary>
    /// <remarks>
    /// An id rather than a closed set, although there are only eleven of them and every pet type
    /// answers all eleven: the names live in <c>pet_command_names</c> where an operator can edit
    /// them, and a second copy enumerated here would be the drift this vocabulary exists to
    /// prevent. The editor offers the directory instead.
    /// </remarks>
    public static readonly FactKey PetCommand = new(
        RewardTrackFacts.PetCommand,
        FactKind.OpaqueId,
        "rewardTracks.fact_pet_command",
        "Pet command"
    );

    /// <summary>A name the player typed.</summary>
    public static readonly FactKey GivenName = new(
        RewardTrackFacts.GivenName,
        FactKind.Text,
        "rewardTracks.fact_given_name",
        "Given name"
    );

    /// <summary>An avatar effect.</summary>
    public static readonly FactKey Effect = new(
        RewardTrackFacts.Effect,
        FactKind.OpaqueId,
        "rewardTracks.fact_effect",
        "Avatar effect"
    );

    /// <summary>How long, in seconds.</summary>
    public static readonly FactKey DurationSeconds = new(
        RewardTrackFacts.DurationSeconds,
        FactKind.Number,
        "rewardTracks.fact_seconds",
        "Duration (s)"
    );

    /// <summary>
    /// A mystery-box colour.
    /// </summary>
    /// <remarks>
    /// Closed, and taken from <see cref="MysteryBoxColors"/> rather than restated: that list is
    /// client truth — the toolbar tints its artwork through a hardcoded dictionary keyed by exactly
    /// these eight names — so a colour typed by hand would be a filter that can never match.
    /// </remarks>
    public static readonly FactKey Colour = new(
        RewardTrackFacts.Colour,
        FactKind.Enum,
        "rewardTracks.fact_colour",
        "Colour",
        [.. MysteryBoxColors.All.Select(c => new EnumValue(c, $"mysteryBox.colour_{c}", c))]
    );

    /// <summary>How many months.</summary>
    public static readonly FactKey Months = new(
        RewardTrackFacts.Months,
        FactKind.Number,
        "rewardTracks.fact_months",
        "Months"
    );

    /// <summary>Serial number in a limited series.</summary>
    public static readonly FactKey Serial = new(
        RewardTrackFacts.Serial,
        FactKind.Number,
        "rewardTracks.fact_serial",
        "Serial number"
    );

    /// <summary>An avatar figure string.</summary>
    public static readonly FactKey Figure = new(
        RewardTrackFacts.Figure,
        FactKind.Text,
        "rewardTracks.fact_figure",
        "Figure"
    );

    /// <summary>
    /// The garments a look is wearing, as figure set ids.
    /// </summary>
    /// <remarks>
    /// Beside <see cref="Figure"/> rather than replacing it: the whole look still answers the
    /// questions a set id cannot — "is barefoot" is the absence of a <c>sh-</c> part, and no set id
    /// describes an absence. What this adds is the exact question: a substring of a figure is not a
    /// garment, and <c>hr-1</c> is a substring of <c>hr-115</c>.
    /// </remarks>
    public static readonly FactKey FigureSet = new(
        RewardTrackFacts.FigureSet,
        FactKind.FigureSetId,
        "rewardTracks.fact_figure_set",
        "Garment worn"
    );

    /// <summary>
    /// Which side of a piece of furniture somebody was standing on.
    /// </summary>
    /// <remarks>
    /// Closed, and measured from the furniture rather than from the room — see
    /// <see cref="RelativePosition"/>. An operator picks a tile on a diagram of the thing rather
    /// than typing a compass direction, which is the only way this reads as what it means: the
    /// front of a chair is wherever the chair is pointing.
    /// </remarks>
    public static readonly FactKey Relation = new(
        RewardTrackFacts.Relation,
        FactKind.Enum,
        "rewardTracks.fact_relation",
        "Where they stood",
        [
            .. Enum.GetValues<RelativePosition>()
                .Select(p => new EnumValue(
                    p.ToValue(),
                    $"rewardTracks.relation_{p.ToValue()}",
                    p.ToString()
                )),
        ]
    );

    /// <summary>
    /// Which dialog saved a room's settings.
    /// </summary>
    /// <remarks>
    /// The three the room grain actually raises. Closed, so "rename a flat" and "re-file it under a
    /// category" are two tasks an operator picks rather than two strings they spell.
    /// </remarks>
    public static readonly FactKey Section = new(
        RewardTrackFacts.Section,
        FactKind.Enum,
        "rewardTracks.fact_section",
        "Section",
        [
            new("settings", "rewardTracks.section_settings", "Main settings"),
            new("tags", "rewardTracks.section_tags", "Tags"),
            new("category_trade", "rewardTracks.section_category_trade", "Category and trading"),
        ]
    );

    /// <summary>Every core fact, in declaration order. The governance test freezes this list.</summary>
    public static readonly ImmutableArray<FactKey> All =
    [
        Item,
        Definition,
        Placement,
        Room,
        RoomName,
        RoomDescription,
        Category,
        Model,
        Player,
        Offer,
        Habbicon,
        Collection,
        Pet,
        Badge,
        ChatMessage,
        RatingPoints,
        Group,
        Thread,
        Price,
        Quantity,
        Poll,
        Quiz,
        Campaign,
        Voucher,
        ClubGift,
        NftProduct,
        VaultCategory,
        TargetedOffer,
        PetType,
        PetCommand,
        GivenName,
        Effect,
        DurationSeconds,
        Colour,
        Months,
        Serial,
        Figure,
        FigureSet,
        Relation,
        Section,
    ];
}
