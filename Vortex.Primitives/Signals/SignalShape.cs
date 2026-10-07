using System.Collections.Immutable;

namespace Vortex.Primitives.Signals;

/// <summary>
/// What one action's signals look like: the facts they may carry, and what their target means.
/// </summary>
/// <remarks>
/// Declared by the translator that produces the action, and read by everything that needs to know
/// what can be filtered — the editor, the content validator, the interest gate. It replaces
/// <c>RewardTrackActionFacts</c>, which described the same thing in a separate file that nothing
/// compared against the handlers.
/// </remarks>
/// <param name="Facts">
/// The union of facts this action's signals may carry. A signal need not carry all of them — a
/// Habbicon used in a private conversation has no room — but nothing outside this list may appear,
/// and every entry must be reachable by some signal.
/// </param>
/// <param name="TargetKind">
/// What <see cref="ProgressSignal.Target"/> is for this action, or null when the action has no
/// target. It is what lets the editor offer a room picker for <c>create_room</c>'s target and a
/// player picker for <c>request_friend</c>'s, from one virtual <c>target</c> fact.
/// </param>
public sealed record SignalShape(
    string Action,
    ImmutableArray<FactKey> Facts,
    FactKind? TargetKind = null
);

/// <summary>
/// Everything the hotel knows how to say, assembled from the translators that are loaded.
/// </summary>
/// <remarks>
/// Lives in Primitives rather than in the project that builds it, so the content validator and the
/// dashboard can depend on it without referencing <c>Vortex.Signals</c> — the dependency would run
/// the wrong way.
/// </remarks>
public interface ISignalVocabulary
{
    /// <summary>Every declared shape, whichever assembly declared it.</summary>
    ImmutableArray<SignalShape> Shapes { get; }

    /// <summary>The shapes for one action. Empty when no loaded translator produces it.</summary>
    ImmutableArray<SignalShape> ShapesFor(string action);

    /// <summary>
    /// Whether this action can carry this fact — the question the content validator asks before
    /// accepting a filter, and the editor before offering one.
    /// </summary>
    /// <remarks>
    /// True for <c>target</c> exactly when the action declares a
    /// <see cref="SignalShape.TargetKind"/>: the host republishes the target under that key, so a
    /// filter on it matches, even though no translator lists it among its facts.
    /// </remarks>
    bool Emits(string action, string factKey);

    /// <summary>The fact behind a key for this action, including the virtual <c>target</c>.</summary>
    FactKey? FactFor(string action, string factKey);
}

/// <summary>
/// Whether any progression system currently cares about an action.
/// </summary>
/// <remarks>
/// The gate that keeps this design from costing anything on a hotel running no content. Room
/// entries, chat lines and walked tiles arrive constantly — the last one once per tile per avatar —
/// and the answer has to be a hash lookup on the calling thread, before a translator allocates
/// anything.
/// </remarks>
public interface ISignalInterest
{
    /// <summary>True if at least one consumer would do something with this action.</summary>
    bool AnyConsumerCares(string action);
}

/// <summary>
/// One consumer's answer to "which actions do you care about right now".
/// </summary>
/// <remarks>
/// A dedicated singleton, never the consumer itself: handlers are not services. The feature
/// processor builds an activator and registers it in the event registry, and the instance is created
/// and dropped per invocation — so a consumer implementing this interface would leave nothing for
/// <see cref="ISignalInterest"/> to reach.
/// </remarks>
public interface ISignalInterestSource
{
    /// <summary>
    /// The live set. Read on every call rather than cached, so publishing content takes effect at
    /// once and there is no invalidation to get wrong.
    /// </summary>
    ImmutableHashSet<string> Actions { get; }
}
