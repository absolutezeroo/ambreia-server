using System.Collections.Generic;

namespace Vortex.Specs.Model;

/// <summary>
/// What a scenario is expected to end with. <see cref="Unknown"/> is a first-class outcome and the
/// default: a scenario whose official result nobody has observed says so, and stays a question the
/// next capture can answer.
/// </summary>
public enum ScenarioOutcome
{
    Unknown = 0,
    Success,
    Rejected,
    Ignored,
    Disconnected,
}

public static class ScenarioOutcomeNames
{
    public static string Wire(this ScenarioOutcome outcome) =>
        outcome switch
        {
            ScenarioOutcome.Success => "success",
            ScenarioOutcome.Rejected => "rejected",
            ScenarioOutcome.Ignored => "ignored",
            ScenarioOutcome.Disconnected => "disconnected",
            _ => "unknown",
        };

    /// <summary>
    /// Whether <paramref name="wire"/> names an outcome.
    /// </summary>
    /// <remarks>
    /// The validator needs this to tell a scenario's <c>status</c> apart from a confidence level,
    /// which is spelled with the same key elsewhere in the tree. Every outcome but this one is
    /// distinct from every confidence name; <c>unknown</c> is both, which is exactly why the two
    /// went uncaught until a capture made a scenario say something else.
    /// </remarks>
    public static bool IsOutcome(string? wire) =>
        wire is "success" or "rejected" or "ignored" or "disconnected" or "unknown";
}

/// <summary>One source's answer to "what happens in this scenario", kept attributed.</summary>
public sealed record ScenarioClaim
{
    public required string Origin { get; init; }

    public required EvidenceAuthority Authority { get; init; }

    public required ScenarioOutcome Outcome { get; init; }

    public IReadOnlyList<string> EmittedPackets { get; init; } = [];

    public required EvidenceRef Evidence { get; init; }
}

public sealed record ScenarioSpec
{
    public required string Id { get; init; }

    public required string FeatureId { get; init; }

    public required string Title { get; init; }

    /// <summary>Preconditions, as <c>subject.property = value</c> pairs.</summary>
    public IReadOnlyDictionary<string, string> Given { get; init; } =
        new Dictionary<string, string>();

    public required string WhenPacket { get; init; }

    /// <summary>
    /// The expected outcome across every source. Stays <see cref="ScenarioOutcome.Unknown"/> until
    /// something better than "our emulator does this" backs it.
    /// </summary>
    public ScenarioOutcome Expected { get; init; } = ScenarioOutcome.Unknown;

    public Confidence Confidence { get; init; } = Confidence.Unknown;

    public IReadOnlyList<ScenarioClaim> Claims { get; init; } = [];

    /// <summary>
    /// Set when the scenario cannot be settled from the sources at hand and needs a capture. The
    /// text says what to capture, so an unknown is actionable rather than decorative.
    /// </summary>
    public string? NeedsEvidence { get; init; }

    /// <summary>
    /// True once the scenario carries enough shape to drive a differential test. The model supports
    /// it from day one even where nothing executes it yet.
    /// </summary>
    public bool Executable { get; init; }
}
