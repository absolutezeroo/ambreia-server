using System.Threading.Tasks;
using Orleans;

namespace Vortex.Primitives.Rpg.Quests.Grains;

/// <summary>
/// One player's quest log (spec M2a §3.3), keyed by player id. Every answer is a
/// <see cref="QuestCodes"/> value. Accept does not check the quest's requirements: they are zone
/// conditions, and the room that offers the quest checks them first.
/// </summary>
public interface IQuestLogGrain : IGrainWithIntegerKey
{
    public Task<QuestLogSnapshot> GetLogAsync();

    public Task<string> AcceptAsync(string questId);

    /// <summary>Hands the quest in: held objectives read again, items consumed, done written, then the rewards.</summary>
    public Task<string> CompleteAsync(string questId);

    public Task<string> AbandonAsync(string questId);

    public Task CountAsync(QuestSignal signal);

    /// <summary>What the player holds changed (an item, a level): held objectives are read again.</summary>
    public Task RefreshHeldAsync();

    public Task<QuestZoneFacts> GetZoneFactsAsync();

    /// <summary>Sends the player the whole log (RpgQuestLog): on entering a zone, the client's copy is rebuilt.</summary>
    public Task PushLogAsync();
}
