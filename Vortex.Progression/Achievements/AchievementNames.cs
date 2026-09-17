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
}
