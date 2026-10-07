using System;
using System.Collections.Immutable;
using System.Linq;
using Vortex.Primitives.Rooms.Enums;

namespace Vortex.Primitives.Signals;

/// <summary>
/// The core fact vocabulary, typed.
/// </summary>
/// <remarks>
/// <para>
/// The keys are persisted vocabulary, like <see cref="SignalActions"/>: the strings are in the
/// database, so a value never changes. They were the reward tracks' fact names (removed in M0).
/// Beside each one is its <see cref="FactKind"/> — the thing the dashboard used to hardcode per
/// page.
/// </para>
/// <para>
/// <c>RoomOwner</c> is deliberately absent from the list an editor sees. Nothing emits it:
/// <c>PlayerEnteredRoomEvent</c> does not carry the owner, and reading it per entry would be a grain
/// call on an arrival path that has been slow before. Its key would be <c>room_owner</c>; it stays out
/// of here until something emits it, so nobody can pick a filter that never matches.
/// </para>
/// </remarks>
public static class Facts
{
    /// <summary>
    /// What the signal is mainly about. Never declared by a translator: the host republishes
    /// <see cref="ProgressSignal.Target"/> under this key, and a shape says what it means for that
    /// action through <see cref="SignalShape.TargetKind"/>.
    /// </summary>
    public const string TargetKey = "target";

    /// <summary>
    /// The room object id of a piece of furniture: the identity that survives a move.
    /// </summary>
    /// <remarks>
    /// Labelled for what it is rather than for what it sounds like. "The furniture" was the obvious
    /// pick in a dropdown and the least useful answer: it names ONE placed object, by an id that
    /// changes the moment somebody picks it up and puts it down. Written as a literal it is a task
    /// about one piece in one room; its real use is as the thing a later step points back at.
    /// </remarks>
    public static readonly FactKey Item = new(
        "item",
        FactKind.OpaqueId,
        "rewardTracks.fact_item",
        "This one placed piece"
    );

    /// <summary>A furniture definition id — the kind of thing, not the thing.</summary>
    public static readonly FactKey Definition = new(
        "def",
        FactKind.FurnitureId,
        "rewardTracks.fact_def",
        "Any furniture of this kind"
    );

    /// <summary>Value of <see cref="Placement"/> for a floor item.</summary>
    public const string PlacementFloor = "floor";

    /// <summary>Value of <see cref="Placement"/> for a wall item.</summary>
    public const string PlacementWall = "wall";

    /// <summary>Floor or wall: the coarse type no definition id can express on its own.</summary>
    public static readonly FactKey Placement = new(
        "kind",
        FactKind.Enum,
        "rewardTracks.fact_kind",
        "Floor or wall",
        [
            new(PlacementFloor, "rewardTracks.placement_floor", "Floor item"),
            new(PlacementWall, "rewardTracks.placement_wall", "Wall item"),
        ]
    );

    /// <summary>A room id.</summary>
    public static readonly FactKey Room = new(
        "room",
        FactKind.RoomId,
        "rewardTracks.fact_room",
        "Room"
    );

    /// <summary>A room's name. Worth filtering only with <c>Contains</c>.</summary>
    public static readonly FactKey RoomName = new(
        "name",
        FactKind.Text,
        "rewardTracks.fact_name",
        "Room name"
    );

    /// <summary>A room's description.</summary>
    public static readonly FactKey RoomDescription = new(
        "desc",
        FactKind.Text,
        "rewardTracks.fact_desc",
        "Room description"
    );

    /// <summary>A navigator flat-category id.</summary>
    public static readonly FactKey Category = new(
        "category",
        FactKind.CategoryId,
        "rewardTracks.fact_category",
        "Category"
    );

    /// <summary>A room model name — the layout.</summary>
    public static readonly FactKey Model = new(
        "model",
        FactKind.Text,
        "rewardTracks.fact_model",
        "Room model"
    );

    /// <summary>Another player's id: the one befriended, traded with.</summary>
    public static readonly FactKey Player = new(
        "player",
        FactKind.PlayerId,
        "rewardTracks.fact_player",
        "The other player"
    );

    /// <summary>A catalogue offer id.</summary>
    public static readonly FactKey Offer = new(
        "offer",
        FactKind.OfferId,
        "rewardTracks.fact_offer",
        "Catalogue offer"
    );

    /// <summary>A pet id.</summary>
    public static readonly FactKey Pet = new(
        "pet",
        FactKind.OpaqueId,
        "rewardTracks.fact_pet",
        "Pet"
    );

    /// <summary>A badge code.</summary>
    public static readonly FactKey Badge = new(
        "badge",
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
        "message",
        FactKind.Text,
        "rewardTracks.fact_message",
        "What was said"
    );

    /// <summary>A guild id.</summary>
    public static readonly FactKey Group = new(
        "group",
        FactKind.OpaqueId,
        "rewardTracks.fact_group",
        "Guild"
    );

    /// <summary>A forum thread id.</summary>
    public static readonly FactKey Thread = new(
        "thread",
        FactKind.OpaqueId,
        "rewardTracks.fact_thread",
        "Forum thread"
    );

    /// <summary>What it cost.</summary>
    public static readonly FactKey Price = new(
        "price",
        FactKind.Number,
        "rewardTracks.fact_price",
        "Price"
    );

    /// <summary>How many.</summary>
    public static readonly FactKey Quantity = new(
        "quantity",
        FactKind.Number,
        "rewardTracks.fact_quantity",
        "Quantity"
    );

    /// <summary>A quest campaign's code.</summary>
    public static readonly FactKey Campaign = new(
        "campaign",
        FactKind.Text,
        "rewardTracks.fact_campaign",
        "Campaign"
    );

    /// <summary>A club gift's product code.</summary>
    public static readonly FactKey ClubGift = new(
        "club_gift",
        FactKind.Text,
        "rewardTracks.fact_club_gift",
        "Club gift"
    );

    /// <summary>A vault income category.</summary>
    public static readonly FactKey VaultCategory = new(
        "vault_category",
        FactKind.Text,
        "rewardTracks.fact_vault_category",
        "Vault category"
    );

    /// <summary>A pet's species.</summary>
    public static readonly FactKey PetType = new(
        "pet_type",
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
        "pet_command",
        FactKind.OpaqueId,
        "rewardTracks.fact_pet_command",
        "Pet command"
    );

    /// <summary>A name the player typed.</summary>
    public static readonly FactKey GivenName = new(
        "given_name",
        FactKind.Text,
        "rewardTracks.fact_given_name",
        "Given name"
    );

    /// <summary>An avatar effect.</summary>
    public static readonly FactKey Effect = new(
        "effect",
        FactKind.OpaqueId,
        "rewardTracks.fact_effect",
        "Avatar effect"
    );

    /// <summary>How long, in seconds.</summary>
    public static readonly FactKey DurationSeconds = new(
        "seconds",
        FactKind.Number,
        "rewardTracks.fact_seconds",
        "Duration (s)"
    );

    /// <summary>How many months.</summary>
    public static readonly FactKey Months = new(
        "months",
        FactKind.Number,
        "rewardTracks.fact_months",
        "Months"
    );

    /// <summary>An avatar figure string.</summary>
    public static readonly FactKey Figure = new(
        "figure",
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
        "figure_set",
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
        "relation",
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
    /// Which of the four dances an avatar performed.
    /// </summary>
    /// <remarks>
    /// The room is the only place that knows it: once the event is raised, the avatar is simply
    /// dancing. Closed, so "do the Rollie" is a value an operator picks rather than a number they
    /// look up — and <c>none</c> is not among them, because stopping is not dancing and the room
    /// raises no gesture for it.
    /// </remarks>
    public static readonly FactKey Dance = new(
        "dance",
        FactKind.Enum,
        "rewardTracks.fact_dance",
        "Which dance",
        [
            .. Enum.GetValues<AvatarDanceType>()
                .Where(d => d != AvatarDanceType.None)
                .Select(d => new EnumValue(
                    d.ToValue(),
                    $"rewardTracks.dance_{d.ToValue()}",
                    d.ToString()
                )),
        ]
    );

    /// <summary>
    /// How the avatar was holding itself when it reached the tile.
    /// </summary>
    /// <remarks>
    /// The fact that makes "seated in front of a certain piece with bare feet" one condition
    /// instead of three steps. Read at the instant the signal is raised, beside the relation and
    /// the look, because all three are claims about the same moment — split across steps, a player
    /// sits down, stands up, walks over, and satisfies every one of them.
    /// </remarks>
    public static readonly FactKey Posture = new(
        "posture",
        FactKind.Enum,
        "rewardTracks.fact_posture",
        "How they were",
        [.. AvatarPostures.All.Select(p => new EnumValue(p, $"rewardTracks.posture_{p}", p))]
    );

    /// <summary>
    /// Which sign an avatar held up.
    /// </summary>
    /// <remarks>
    /// A number, not a closed set, and deliberately: the client draws 0–17, of which 0–10 are the
    /// digits and 11–17 are specials this repository has never named. A list of eighteen values
    /// where seven read "Special 14" is worse than a number, and naming them from memory is how a
    /// picker comes to offer a heart that is really a skull.
    ///
    /// Ordering is useful here rather than nonsense: "at least 11" is "a special", which is the
    /// one question the numbering does answer.
    ///
    /// When somebody confirms the artwork names, this becomes an Enum over the same values and no
    /// stored task changes.
    /// </remarks>
    public static readonly FactKey Sign = new(
        "sign",
        FactKind.Number,
        "rewardTracks.fact_sign",
        "Which sign"
    );

    /// <summary>
    /// Which dialog saved a room's settings.
    /// </summary>
    /// <remarks>
    /// The three the room grain actually raises. Closed, so "rename a flat" and "re-file it under a
    /// category" are two tasks an operator picks rather than two strings they spell.
    /// </remarks>
    public static readonly FactKey Section = new(
        "section",
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
        Pet,
        Badge,
        ChatMessage,
        Group,
        Thread,
        Price,
        Quantity,
        Campaign,
        ClubGift,
        VaultCategory,
        PetType,
        PetCommand,
        GivenName,
        Effect,
        DurationSeconds,
        Months,
        Figure,
        FigureSet,
        Relation,
        Section,
        Dance,
        Sign,
        Posture,
    ];
}
