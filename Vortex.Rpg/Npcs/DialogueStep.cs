using Vortex.Rpg.Dialogues;

namespace Vortex.Rpg.Npcs;

/// <summary>What a choice did (spec M2a §3.3): nothing (a stale or hidden choice), a node to show, or the end, with a quest result.</summary>
public sealed record DialogueStep
{
    public static readonly DialogueStep Ignored = new() { IsIgnored = true };

    public bool IsIgnored { get; private init; }

    public DialogueNodeView? Next { get; private init; }

    /// <summary>accepted or completed; null for a plain close.</summary>
    public string? Action { get; private init; }

    public string Code { get; private init; } = string.Empty;

    public string QuestId { get; private init; } = string.Empty;

    public static DialogueStep Show(DialogueNodeView next) => new() { Next = next };

    public static DialogueStep Close(string? action, string code, string questId) =>
        new()
        {
            Action = action,
            Code = code,
            QuestId = questId,
        };
}
