using System.Threading.Tasks;
using Vortex.Rpg.Dialogues;

namespace Vortex.Rpg.Npcs;

/// <summary>
/// What an NPC's dialogue says and does (spec M2a §3.3), behind which the room needs to know nothing of
/// content or quests: the room keeps the NPC, the reach and the session; this answers. Declared by the
/// quests module.
/// </summary>
public interface INpcDialogues
{
    /// <summary>The node the dialogue opens at for this player, or null when it has none; the talk is counted for quests.</summary>
    public Task<DialogueNodeView?> OpenAsync(NpcConversation conversation);

    /// <summary>The choice at <paramref name="choiceIndex"/> of the content's node, if this player may make it.</summary>
    public Task<DialogueStep> ChooseAsync(
        NpcConversation conversation,
        string nodeId,
        int choiceIndex
    );

    /// <summary>available, ready, active or empty: what this NPC has for this player.</summary>
    public string Marker(NpcConversation conversation);
}
