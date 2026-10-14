using Vortex.Rpg.Dialogues;

namespace Vortex.Rooms.Grains.Modules;

/// <summary>A player's open dialogue: with which NPC, and the node last shown (its choices are what positions map to).</summary>
internal sealed record DialogueSession(
    int NpcObjectId,
    string NpcId,
    string DialogueId,
    DialogueNodeView Shown
);
