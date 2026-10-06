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

    /// <summary>"For having visitors spend %limit% minutes in your rooms" — credited to the owner.</summary>
    public const string RoomDecoHosting = "RoomDecoHosting";
    public const string RespectGiven = "RespectGiven";
    public const string RespectEarned = "RespectEarned";
    public const string Name = "Name";
    public const string GiftGiver = "GiftGiver";
    public const string PetLevelUp = "PetLevelUp";
    public const string PetLover = "PetLover";

    public const string PetRespectGiver = "PetRespectGiver";
    public const string PetRespectReceiver = "PetRespectReceiver";
    public const string GiftReceiver = "GiftReceiver";
}
