using System.Text.Json.Serialization;

namespace Vortex.Rpg.Content.Definitions;

/// <summary>An answer (spec M2a §2.2): it does one thing — another node, take a quest, hand one in — or closes.</summary>
public sealed record DialogueChoiceDefinition
{
    public required string Text { get; init; }

    public string? Next { get; init; }

    public string? Accept { get; init; }

    public string? Complete { get; init; }

    /// <summary>next, accept, complete or close: what the client's button looks like.</summary>
    [JsonIgnore]
    public string Kind =>
        Accept is not null ? "accept"
        : Complete is not null ? "complete"
        : Next is not null ? "next"
        : "close";

    /// <summary>The quest this choice takes or hands in.</summary>
    [JsonIgnore]
    public string? Quest => Accept ?? Complete;
}
