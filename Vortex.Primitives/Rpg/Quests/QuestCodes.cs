namespace Vortex.Primitives.Rpg.Quests;

/// <summary>A quest action's answer (spec M2a §3.2, RpgQuestResult): ok, or the refusal the client translates.</summary>
public static class QuestCodes
{
    public const string Ok = "ok";
    public const string LogFull = "quest.log_full";
    public const string NotOfferable = "quest.not_offerable";
    public const string NotReady = "quest.not_ready";
    public const string NotInLog = "quest.not_in_log";

    /// <summary>The wire and database spelling of a status: none, active, ready, done.</summary>
    public static string Name(QuestStatus status) => status.ToString().ToLowerInvariant();
}
