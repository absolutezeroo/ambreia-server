using System;
using Vortex.Primitives.Rpg.Quests;

namespace Vortex.Rpg.Quests.Log;

/// <summary>One quest of a player's log: its status and one counter per objective.</summary>
public sealed class QuestEntry
{
    public required string QuestId { get; init; }

    public required QuestStatus Status { get; set; }

    public required int[] Progress { get; set; }

    public required DateTime AcceptedAt { get; init; }

    public DateTime? CompletedAt { get; set; }
}
