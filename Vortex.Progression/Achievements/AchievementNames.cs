namespace Vortex.Progression.Achievements;

/// <summary>
/// Canonical achievement identifiers (the <c>name</c> column of the <c>achievements</c> table, which
/// also builds badge codes as <c>"ACH_" + name + level</c>). Referenced by the progression triggers
/// so trigger sites never hardcode a raw string that could drift from the seeded definitions.
/// </summary>
public static class AchievementNames
{
    public const string Login = "Login";
    public const string RoomEntry = "RoomEntry";
    public const string Motto = "Motto";
    public const string AvatarLooks = "AvatarLooks";
    public const string FriendListSize = "FriendListSize";
    public const string RoomDecoFurniCount = "RoomDecoFurniCount";
    public const string RespectGiven = "RespectGiven";
    public const string RespectEarned = "RespectEarned";
    public const string Name = "Name";
    public const string GiftGiver = "GiftGiver";
    public const string PetLevelUp = "PetLevelUp";
    public const string PetLover = "PetLover";

    /// <summary>Freeze's participation points. The hotel's own name for it, not a typo.</summary>
    public const string FreezePlayer = "FreezePlayer";
    public const string BattleBallPlayer = "BattleBallPlayer";
    public const string FreezePowerUp = "FreezePowerUp";

    /// <summary>"For freezing %limit% Habbos in Freeze" — Sulake's code name for it is ACH_EsA.</summary>
    public const string EsA = "EsA";
    public const string FootballGoalScored = "FootballGoalScored";

    /// <summary>Colours of one baby pet type owned. See <see cref="BabyPetAchievements"/>.</summary>
    public const string BearBreeder = "BearBreeder";
    public const string TerrierBreeder = "TerrierBreeder";
    public const string CatBreeder = "CatBreeder";
    public const string DogBreeder = "DogBreeder";
    public const string PigBreeder = "PigBreeder";
}
