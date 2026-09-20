using System.Collections.Immutable;

namespace Vortex.Primitives.Signals;

/// <summary>
/// A named fact a signal can carry, and everything an editor needs to let an operator filter on it.
/// </summary>
/// <remarks>
/// <para>
/// The kind is what makes a filter editor generic. Before it, the dashboard hardcoded which facts
/// got a picker and which got a text box, and a floor/wall filter was a select written by hand on
/// one page — so any other system wanting filters would have had to write all of it again.
/// </para>
/// <para>
/// The key is stored in content (<c>reward_track_step_filters.fact_key</c> and the equivalent
/// columns of any later consumer). It is therefore append-only: a renamed key silently stops
/// matching every filter already written on it, which no build and no test would notice. Add keys,
/// never rename or remove them.
/// </para>
/// </remarks>
/// <param name="LabelKey">Dashboard locale key.</param>
/// <param name="FallbackLabel">
/// Shown when the locale key resolves to nothing. A plugin cannot add keys to the dashboard's
/// locales, so without this its facts would render as raw keys; the core fills it too, which keeps
/// the editor readable when a translation lags behind.
/// </param>
/// <param name="EnumValues">
/// The allowed values, for <see cref="FactKind.Enum"/> only. A closed fact with no values declared
/// is a select with no options, and is refused when the vocabulary is built.
/// </param>
public sealed record FactKey(
    string Key,
    FactKind Kind,
    string LabelKey,
    string FallbackLabel,
    ImmutableArray<EnumValue> EnumValues = default
);

/// <summary>
/// Which operators make sense on a fact, decided by its kind.
/// </summary>
/// <remarks>
/// <para>
/// An exact match on free text a player typed is a filter that never fires; a substring match on a
/// room id is nonsense. Neither is caught by the engine, which will happily evaluate both and
/// silently never match — so the rule is enforced where content is written instead.
/// </para>
/// <para>
/// Lives here rather than in the editor because the editor is not the only way in: content also
/// arrives through the operations API, and a rule only the UI knows is a rule content can dodge.
/// </para>
/// </remarks>
public static class FactOperators
{
    /// <summary>Substring, the only operator worth anything on free text.</summary>
    public const int Contains = 3;

    /// <summary>Its negation, which is the only way to say a part is absent.</summary>
    public const int NotContains = 6;

    private static readonly ImmutableArray<int> Equality = [0, 1];
    private static readonly ImmutableArray<int> EqualityAndList = [0, 1, 2, 7];

    // A closed set gets the list operators too. "Anywhere in front of it" is three of the nine
    // sides and one filter; without these it was three tasks, or a filter per side.
    private static readonly ImmutableArray<int> EnumOperators = [0, 1, 2, 7];

    // A fact that is itself a set answers neither "is it this" nor "is it one of these" -- both
    // ask about a single value. It answers membership, in both directions, and nothing else.
    private static readonly ImmutableArray<int> MembershipOperators = [8, 9];

    // Ordering, and only for numbers. An id compared with "at least" is nonsense that would
    // evaluate cleanly and mean nothing, which is exactly the class of filter this table exists
    // to keep out of the editor.
    private static readonly ImmutableArray<int> NumberOperators = [0, 1, 4, 5];
    private static readonly ImmutableArray<int> TextOperators = [Contains, NotContains, 0, 1];

    /// <summary>The operators an editor may offer, and a validator must accept, for this kind.</summary>
    public static ImmutableArray<int> For(FactKind kind) =>
        kind switch
        {
            FactKind.Text => TextOperators,
            FactKind.Number => NumberOperators,
            FactKind.Enum => EnumOperators,
            FactKind.OpaqueId => Equality,
            FactKind.FigureSetId => MembershipOperators,
            _ => EqualityAndList,
        };

    /// <summary>Whether this operator says anything meaningful about this kind of fact.</summary>
    public static bool Allows(FactKind kind, int op) => For(kind).Contains(op);
}

/// <summary>One allowed value of a closed fact: what the engine compares, and what the operator reads.</summary>
public readonly record struct EnumValue(string Value, string LabelKey, string FallbackLabel);

/// <summary>
/// What a fact's value is, which decides the control an editor shows and the operators it offers.
/// </summary>
public enum FactKind
{
    /// <summary>Free text — a room name, a description, a motto. Only <c>Contains</c> is much use.</summary>
    Text = 0,

    /// <summary>A count, a level, a total. May legitimately be zero.</summary>
    Number = 1,

    /// <summary>A player id. Offers the player picker.</summary>
    PlayerId = 2,

    /// <summary>A room id. Offers the room picker.</summary>
    RoomId = 3,

    /// <summary>A furniture definition id. Offers the furniture picker, with its sprite.</summary>
    FurnitureId = 4,

    /// <summary>A navigator flat-category id. Offers the category list.</summary>
    CategoryId = 5,

    /// <summary>A badge code.</summary>
    BadgeCode = 6,

    /// <summary>A catalogue offer id.</summary>
    OfferId = 7,

    /// <summary>A closed set of values, enumerated in <see cref="FactKey.EnumValues"/>.</summary>
    Enum = 8,

    /// <summary>
    /// A live id with no directory behind it — a placed item, a pet. Typed by hand, and mostly
    /// useful as the target of a <c>$N</c> back-reference rather than as a literal.
    /// </summary>
    OpaqueId = 9,

    /// <summary>
    /// A set of figure set ids: what a look is wearing, not one value. Offers the garment picker,
    /// and the only two operators that mean anything about a set — is one of these in it, is none
    /// of them.
    /// </summary>
    FigureSetId = 10,
}
