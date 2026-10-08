namespace Vortex.Primitives.Rpg.Items;

/// <summary>The answer to an item operation (spec Objets §7): the web API and the socket send it
/// as a snake_case code. A refusal changes nothing.</summary>
public enum ItemOutcome
{
    Ok = 0,
    NoCharacter = 1,
    NotFound = 2,
    BagFull = 3,
    SlotOccupied = 4,
    WrongClass = 5,
    LevelTooLow = 6,
    NotEquipment = 7,
    InvalidQuantity = 8,
    ParcelsFull = 9,
    DuplicateGrant = 10,
    UnknownItem = 11,
}
