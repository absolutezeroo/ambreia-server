using System;

namespace Vortex.Primitives.World.Editing;

/// <summary>
/// A zone as the editor's zone list shows it. Type, region, size and template come from the
/// newest version's settings and floor; author and date are that version's. All empty for a zone
/// never saved (or whose newest document no longer parses).
/// </summary>
public sealed record ZoneSummary(
    string Key,
    string Name,
    int RoomId,
    int? PublishedVersion,
    int LatestVersion,
    int? LockedByAccountId,
    string Type = "",
    string Region = "",
    int Width = 0,
    int Length = 0,
    bool Template = false,
    string AuthorName = "",
    DateTime? ModifiedAt = null
);
