using System.Collections.Immutable;
using System.Globalization;
using System.Linq;

namespace Vortex.Primitives.Signals;

/// <summary>
/// Builds a signal's facts, and encodes the two rules that are easy to get wrong by hand.
/// </summary>
/// <remarks>
/// <para>
/// <b>An absent identifier is omitted, not written as zero.</b> A Habbicon used in a private
/// conversation carries room 0, and a room filed under no category carries category 0. Emitting
/// those as facts would make "any room but 12" match a private conversation, because a filter only
/// fails closed when the fact is genuinely absent.
/// </para>
/// <para>
/// <b>The rule stops at identifiers.</b> A count of zero is a value, not an absence — which is why
/// <see cref="Number"/> writes it and <see cref="IdIfAny"/> does not. Two method names instead of one
/// judgement call at each call site.
/// </para>
/// </remarks>
public readonly struct SignalFacts
{
    private readonly ImmutableArray<SignalFact>.Builder? _builder;

    private SignalFacts(ImmutableArray<SignalFact>.Builder builder) => _builder = builder;

    /// <summary>Starts a fact list.</summary>
    public static SignalFacts Build() => new(ImmutableArray.CreateBuilder<SignalFact>());

    /// <summary>An identifier that is always present.</summary>
    public SignalFacts Id(FactKey key, int value) => Add(key.Key, Format(value));

    /// <inheritdoc cref="Id(FactKey, int)"/>
    public SignalFacts Id(FactKey key, long value) => Add(key.Key, Format(value));

    /// <summary>An identifier that is absent when it is zero.</summary>
    public SignalFacts IdIfAny(FactKey key, int value) =>
        value == 0 ? this : Add(key.Key, Format(value));

    /// <inheritdoc cref="IdIfAny(FactKey, int)"/>
    public SignalFacts IdIfAny(FactKey key, long value) =>
        value == 0 ? this : Add(key.Key, Format(value));

    /// <summary>A number, including zero.</summary>
    public SignalFacts Number(FactKey key, int value) => Add(key.Key, Format(value));

    /// <summary>Text, omitted when null or blank — an empty description is nothing to filter on.</summary>
    public SignalFacts Text(FactKey key, string? value) =>
        string.IsNullOrWhiteSpace(value) ? this : Add(key.Key, value);

    /// <summary>One of a closed set. The value must be declared in the key's enum values.</summary>
    public SignalFacts Enum(FactKey key, string value) => Add(key.Key, value);

    /// <summary>
    /// Several identifiers at once, comma-separated, omitted when there are none.
    /// </summary>
    /// <remarks>
    /// The separator is the one <c>OneOf</c> already uses, so the two read the same list. Omitted
    /// when empty for the reason <see cref="IdIfAny"/> is: an empty set is an absence, and a filter
    /// only fails closed when the fact is genuinely not there.
    /// </remarks>
    public SignalFacts Ids(FactKey key, ImmutableArray<int> values) =>
        values.IsDefaultOrEmpty
            ? this
            : Add(key.Key, string.Join(',', values.Select(v => Format(v))));

    /// <summary>The finished list.</summary>
    public ImmutableArray<SignalFact> ToImmutable() =>
        _builder is null ? [] : _builder.ToImmutable();

    /// <summary>Lets a builder be passed where the facts themselves are expected.</summary>
    public static implicit operator ImmutableArray<SignalFact>(SignalFacts facts) =>
        facts.ToImmutable();

    private SignalFacts Add(string key, string value)
    {
        _builder?.Add(new SignalFact(key, value));

        return this;
    }

    /// <summary>Invariant form: the same content must match the same ids on every silo.</summary>
    private static string Format(long value) => value.ToString(CultureInfo.InvariantCulture);
}
