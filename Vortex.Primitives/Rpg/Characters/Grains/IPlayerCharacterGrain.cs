using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Rooms;

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

    /// <summary>
    /// Gives the avatar its class: the starter class « Sans classe » or a real one when it has none,
    /// then, once, a real class in place of the starter, keeping level and XP. A real class is
    /// permanent (owner decision, 2026-10-08).
    /// </summary>
    public Task<ChooseClassOutcome> ChooseClassAsync(string classId);

    /// <summary>The computed stats; null without a character.</summary>
    public Task<CharacterStatsSnapshot?> GetStatsAsync();

    /// <summary>Grants XP, server side only (spec Personnage §5, §14): levels chained, a level
    /// written at once.</summary>
    public Task<XpGrantOutcome> GainXpAsync(int amount, string source);

    /// <summary>The character entered this room: kept as its last zone if it is a world zone (never
    /// a dungeon copy), written at the next flush; then its state is pushed to the player.</summary>
    public Task EnteredRoomAsync(RoomId roomId);

    /// <summary>The whole state (class, level, XP, stats) to the player's socket, for the HUD (spec
    /// F3a §5.6); nothing without a character.</summary>
    public Task PushStateAsync();

    /// <summary>The world zone to sign in to (spec Personnage §9); null without a character or a zone.</summary>
    public Task<RoomId?> GetLastRoomAsync();
}
