using System.Threading.Tasks;
using Orleans;

namespace Vortex.Primitives.Rpg.Characters.Grains;

/// <summary>
/// The RPG state of one avatar (spec Personnage §7), keyed by player id. Its sole owner: the table
/// <c>player_characters</c> is written through it, never directly. An avatar without a class has
/// no character, and every call answers as such.
/// </summary>
public interface IPlayerCharacterGrain : IGrainWithIntegerKey
{
    /// <summary>Class, specialization, level and XP; null without a character.</summary>
    public Task<CharacterSummary?> GetSummaryAsync();

    /// <summary>Gives the avatar its class, once (spec Personnage §2.4).</summary>
    public Task<ChooseClassOutcome> ChooseClassAsync(string classId);

    /// <summary>The computed stats; null without a character.</summary>
    public Task<CharacterStatsSnapshot?> GetStatsAsync();
}
