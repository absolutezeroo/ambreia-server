using System.Collections.Immutable;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rpg.Items;

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

    /// <summary>An operator's grant: <paramref name="grantKey"/> already seen gives nothing more
    /// (Duplicate), the day's grants are capped (DailyCapReached), and the XP is written at once.</summary>
    public Task<XpGrantOutcome> GrantXpAsync(string grantKey, int amount, string source);

    /// <summary>The character entered this room: kept as its last zone if it is a world zone (never
    /// a dungeon copy), written at the next flush; then its state is pushed to the player.</summary>
    public Task EnteredRoomAsync(RoomId roomId);

    /// <summary>The whole state (class, level, XP, stats) to the player's socket, for the HUD (spec
    /// F3a §5.6); nothing without a character.</summary>
    public Task PushStateAsync();

    /// <summary>The world zone to sign in to (spec Personnage §9); null without a character or a zone.</summary>
    public Task<RoomId?> GetLastRoomAsync();

    // Items (spec Objets §7): every call answers NoCharacter, or nothing, for an avatar without a
    // class. A refusal changes nothing.

    /// <summary>Server side only: <paramref name="grantKey"/> already seen grants nothing more
    /// (DuplicateGrant); the items and the key are written together, at once.</summary>
    public Task<ItemGrantResult> GrantItemsAsync(string grantKey, ItemGrantLine[] items);

    /// <summary>Server side only (quests, crafting): from the bag's stacks, all or nothing.</summary>
    public Task<ItemOutcome> RemoveItemsAsync(string definitionId, int quantity);

    public Task<ItemOutcome> MoveItemAsync(int from, int to);

    public Task<ItemOutcome> SplitItemAsync(int from, int quantity, int to);

    public Task<ItemOutcome> EquipItemAsync(int bagSlot);

    public Task<ItemOutcome> UnequipItemAsync(int equipmentSlot);

    public Task<ItemOutcome> DestroyItemAsync(int bagSlot, int quantity);

    public Task<ItemOutcome> ClaimParcelAsync(int parcel);

    /// <summary>Stats, current health and mana (out-of-zone regeneration applied, bounded to the
    /// maximum), level, range and threat multiplier; null without a character (spec M1a §7).</summary>
    public Task<CharacterCombatProfile?> GetCombatProfileAsync();

    /// <summary>The zone gives health and mana back when the player leaves it: one write (spec M1a
    /// §4.4), bounded to the maximum; with the persistent statuses he carries out (spec M1b §7.4),
    /// kept in memory only.</summary>
    public Task SetVitalsAsync(int health, int mana, ImmutableArray<CarriedStatus> statuses);

    /// <summary>Puts an action (<c>skill:&lt;id&gt;</c>, or "" to clear) in a slot of the bar (spec M1b
    /// §9); the player is told the new bar, or why not.</summary>
    public Task SetActionBarAsync(int slot, string actionId);

    /// <summary>Equipped items lose <paramref name="percent"/> % of their max durability (death,
    /// spec M1a §8.1).</summary>
    public Task WearEquipmentAsync(int percent);

    /// <summary>Every copy held: bag, equipment, parcels.</summary>
    public Task<CharacterItemSnapshot[]> GetItemsAsync();

    /// <summary>What a zone's rules read of this character (spec Z1 §4.3): its level and what it
    /// owns. Null without a character.</summary>
    public Task<CharacterZoneFacts?> GetZoneFactsAsync();
}
