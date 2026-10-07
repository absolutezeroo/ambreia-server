namespace Vortex.Primitives.World.Editing;

/// <summary>A zone as the editor's zone list shows it.</summary>
public sealed record ZoneSummary(
    string Key,
    string Name,
    int RoomId,
    int? PublishedVersion,
    int LatestVersion,
    int? LockedByAccountId
);
