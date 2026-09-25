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
    public const string RoomDecoFurniTypeCount = "RoomDecoFurniTypeCount";
    public const string RoomDecoWallpaper = "RoomDecoWallpaper";
    public const string RoomDecoFloor = "RoomDecoFloor";
    public const string RoomDecoLandscape = "RoomDecoLandscape";

    /// <summary>"For making Swiss cheese by adding %limit% floor hole furni".</summary>
    public const string RoomDecoHoleFurniCount = "RoomDecoHoleFurniCount";

    /// <summary>"For having %limit% days of Builders Club membership".</summary>
    public const string BuildersClub = "BuildersClub";

    /// <summary>"For placing %limit% Builders Club furni".</summary>
    public const string RoomDecoBC = "RoomDecoBC";

    /// <summary>"For having visitors spend %limit% minutes in your rooms" — credited to the owner.</summary>
    public const string RoomDecoHosting = "RoomDecoHosting";
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

    public const string PetRespectGiver = "PetRespectGiver";
    public const string PetRespectReceiver = "PetRespectReceiver";
    public const string PetFeeding = "PetFeeding";
    public const string GiftReceiver = "GiftReceiver";
}
