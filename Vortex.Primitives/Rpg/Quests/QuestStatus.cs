using Orleans;

namespace Vortex.Primitives.Rpg.Quests;

/// <summary>A quest for one player (spec M2a §2.2): not in the log, under way, all objectives met, handed in.</summary>
[GenerateSerializer]
public enum QuestStatus
{
    None = 0,
    Active = 1,
    Ready = 2,
    Done = 3,
}
