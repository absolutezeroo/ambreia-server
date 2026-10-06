namespace Vortex.Primitives.RewardTracks;

/// <summary>
/// The named facts a progress signal can carry about what just happened.
/// </summary>
/// <remarks>
/// <para>
/// A signal used to be one opaque string, which is why a task could say "a sofa" but never "the
/// sofa you just placed". Facts are what make a sequence composable: a step filters on them, and a
/// later step can point back at what an earlier one matched.
/// </para>
/// <para>
/// The vocabulary is closed on purpose. An operator picking a fact an action never emits would be
/// writing a filter that silently never matches, so the dashboard offers only the facts the chosen
/// action actually produces — see <c>ISignalVocabulary</c>, which is assembled from the translators
/// that emit them rather than from a list kept in step by hand.
/// </para>
/// </remarks>
public static class RewardTrackFacts
{
    /// <summary>
    /// What the signal was mainly about. Every signal that carries anything carries this, and it is
    /// what a task's <c>Parameter</c> is matched against — which is why the parameter keeps working
    /// unchanged.
    /// </summary>
    public const string Target = "target";

    /// <summary>The room object id of a piece of furniture: the identity that survives a move.</summary>
    public const string Item = "item";

    /// <summary>A furniture definition id — the kind of thing, not the thing.</summary>
    public const string Definition = "def";

    /// <summary>
    /// <c>floor</c> or <c>wall</c>. The coarse type an operator means by "un mobilier de type sol",
    /// which no definition id can express on its own.
    /// </summary>
    public const string Placement = "kind";

    /// <summary>A room id.</summary>
    public const string Room = "room";

    /// <summary>
    /// A room's name. Only worth filtering with <c>Contains</c>: a room id says which room, a name
    /// says what kind of room, and "build a flat with 'casino' in the name" is a task a room id
    /// cannot express — the id of a room created a moment ago is knowable to nobody.
    /// </summary>
    public const string RoomName = "name";

    /// <summary>A room's description. Same reasoning as <see cref="RoomName"/>.</summary>
    public const string RoomDescription = "desc";

    /// <summary>A navigator flat-category id, or <c>0</c> when the room names none.</summary>
    public const string Category = "category";

    /// <summary>A room model name — the layout, e.g. <c>model_a</c>.</summary>
    public const string Model = "model";

    /// <summary>
    /// Who owns the room. Reserved, and NOT emitted by anything yet, though it is now closer than
    /// this note used to say: <c>PlayerEnteredRoomEvent</c> gained an <c>OwnerId</c> parameter so
    /// that "join their flat" would be expressible without a follow event, but its only publish site
    /// (<c>PlayerPresenceGrain.Room</c>) does not pass it, so it is always zero. Populating it there
    /// is the two-line job that makes this fact honest; until then it stays out of the signal
    /// vocabulary, because a fact that is always absent is a filter that silently never matches.
    /// </summary>
    public const string RoomOwner = "room_owner";

    /// <summary>Another player's id: the one respected, befriended, traded with.</summary>
    public const string Player = "player";

    /// <summary>A catalogue offer id.</summary>
    public const string Offer = "offer";

    /// <summary>A pet id.</summary>
    public const string Pet = "pet";

    /// <summary>A badge code.</summary>
    public const string Badge = "badge";

    /// <summary>What a chat line actually said. Filtered with <c>Contains</c>, never equality.</summary>
    public const string ChatMessage = "message";

    /// <summary>A rating's direction: <c>+1</c> or <c>-1</c>.</summary>
    public const string RatingPoints = "points";

    // The vocabulary the wider coverage needs. Each is a fact some event already carries: nothing
    // here costs a read, and anything that would have is left undeclared rather than faked.

    /// <summary>A guild id.</summary>
    public const string Group = "group";

    /// <summary>A forum thread id.</summary>
    public const string Thread = "thread";

    /// <summary>What something cost, in whatever currency the action spends.</summary>
    public const string Price = "price";

    /// <summary>How many of a thing.</summary>
    public const string Quantity = "quantity";

    // "currency" was here. No event carries one: every price the hotel raises is in the single
    // currency its action spends, so the fact would have been a constant, and a filter on a
    // constant is a filter that changes nothing. Removed rather than fabricated -- the rule this
    // subsystem exists for cuts both ways.

    /// <summary>A poll's code.</summary>
    public const string Poll = "poll";

    /// <summary>A quiz's code.</summary>
    public const string Quiz = "quiz";

    /// <summary>A quest campaign's code.</summary>
    public const string Campaign = "campaign";

    /// <summary>A voucher's code.</summary>
    public const string Voucher = "voucher";

    /// <summary>A club gift's product code.</summary>
    public const string ClubGift = "club_gift";

    /// <summary>A vault income category.</summary>
    public const string VaultCategory = "vault_category";

    /// <summary>A targeted offer's identifier.</summary>
    public const string TargetedOffer = "targeted_offer";

    /// <summary>A pet's species number.</summary>
    public const string PetType = "pet_type";

    /// <summary>The order a pet obeyed, as the client's training buttons number them.</summary>
    public const string PetCommand = "pet_command";

    /// <summary>A name a player typed: a pet's, a guild's.</summary>
    public const string GivenName = "given_name";

    /// <summary>An avatar effect id.</summary>
    public const string Effect = "effect";

    /// <summary>A duration in seconds — a stay, an effect, a session.</summary>
    public const string DurationSeconds = "seconds";

    /// <summary>A colour name, as the mystery-box vocabulary uses it.</summary>
    public const string Colour = "colour";

    /// <summary>A count of months.</summary>
    public const string Months = "months";

    /// <summary>A serial number within a limited series.</summary>
    public const string Serial = "serial";

    /// <summary>An avatar figure string.</summary>
    public const string Figure = "figure";

    /// <summary>
    /// The figure set ids a look is wearing, comma-separated.
    /// </summary>
    /// <remarks>
    /// The same look as <see cref="Figure"/>, taken apart. It exists because a substring of a
    /// figure is not a garment: real players in this hotel wear <c>hr-1-64</c> and
    /// <c>hr-115-42</c>, so "contains hr-1" matches both. Comparing set ids compares the thing
    /// figuredata actually names.
    /// </remarks>
    public const string FigureSet = "figure_set";

    /// <summary>
    /// Where somebody was standing in relation to a piece of furniture, from the furniture's own
    /// point of view: <c>on</c>, <c>front</c>, <c>back_left</c> and the rest.
    /// </summary>
    public const string Relation = "relation";

    /// <summary>
    /// Which dance it was: <c>dance</c>, <c>pogo_mogo</c>, <c>duck_funk</c> or <c>the_rollie</c>.
    /// </summary>
    public const string Dance = "dance";

    /// <summary>Which sign was held up: <c>0</c>–<c>10</c> are the digits, <c>11</c>–<c>17</c> the specials.</summary>
    public const string Sign = "sign";

    /// <summary>How the avatar was holding itself: <c>standing</c>, <c>sitting</c> or <c>laying</c>.</summary>
    public const string Posture = "posture";

    /// <summary>Which setting or section an edit touched.</summary>
    public const string Section = "section";

    /// <summary>Value of <see cref="Placement"/> for a floor item.</summary>
    public const string PlacementFloor = "floor";

    /// <summary>Value of <see cref="Placement"/> for a wall item.</summary>
    public const string PlacementWall = "wall";
}
