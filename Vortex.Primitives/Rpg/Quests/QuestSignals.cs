namespace Vortex.Primitives.Rpg.Quests;

/// <summary>The signals shipped with M2a; a module adding one adds its constant where it lives.</summary>
public static class QuestSignals
{
    /// <summary>Subject: monster (content id), rank (normal, elite, boss).</summary>
    public const string MonsterKilled = "monster_killed";

    /// <summary>Subject: area (a zone area id).</summary>
    public const string AreaEntered = "area_entered";

    /// <summary>Subject: object (a zone decor id).</summary>
    public const string DecorUsed = "decor_used";

    /// <summary>Subject: npc (a zone npc id).</summary>
    public const string NpcTalked = "npc_talked";
}
