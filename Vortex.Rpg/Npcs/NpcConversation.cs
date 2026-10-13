using System;
using System.Collections.Generic;
using Vortex.Primitives.Players;
using Vortex.Primitives.World.Rules;

namespace Vortex.Rpg.Npcs;

/// <summary>
/// One player talking to one NPC (spec M2a §3.3), as the room hands it over: who, in which zone (its
/// key), which NPC (its zone id) and dialogue, the player's quest states (quest id → none, active,
/// ready, done; a quest absent is none), and the room's rules for a quest's requirements.
/// </summary>
public sealed record NpcConversation(
    PlayerId Player,
    string Zone,
    string NpcId,
    string DialogueId,
    IReadOnlyDictionary<string, string> QuestStates,
    Func<IReadOnlyList<ZoneCondition>, bool> AllHold
);
