namespace Vortex.Primitives.RewardTracks;

/// <summary>
/// Where one side of a comparison gets its value.
/// </summary>
/// <remarks>
/// <para>
/// A test used to be "this fact, this operator, this literal" — a shape that could only ever ask
/// about one fact against something typed. Both sides being a value of one of these kinds is what
/// lets a condition compare two facts, or an earlier step's fact, without the engine learning a
/// new question each time.
/// </para>
/// <para>
/// Every kind here is <em>pure</em>: facts in, a string out, no lookup. That is not a style
/// preference. This is walked on the room's event path, once per signal, for every player in the
/// room — a kind that went to the database would make a popular campaign a load problem, and the
/// failure would arrive as room lag rather than as anything pointing here. A condition that needs
/// something the signal does not carry is answered by teaching the translator to emit a new fact,
/// never by teaching this to fetch one.
/// </para>
/// </remarks>
public enum StepValueKind
{
    /// <summary>What the signal carried for a fact key. The left side of every test written so far.</summary>
    Fact = 0,

    /// <summary>Text an operator typed. The right side of every test written so far.</summary>
    Literal = 1,

    /// <summary>
    /// What an earlier step recorded for a fact key — "the furniture you placed in step 1".
    /// </summary>
    /// <remarks>
    /// The generalisation of <c>$N</c>, which could only ever read the <em>same</em> fact key as
    /// the side it was compared against. Naming the key lets a step's room be compared with a
    /// later step's room owner, which <c>$N</c> cannot express at all.
    /// </remarks>
    StepFact = 2,

    /// <summary>
    /// How many entries a list-valued fact carried — a look wears several garments, and "at least
    /// three" is a question about the count rather than about any one of them.
    /// </summary>
    CountOf = 3,
}
