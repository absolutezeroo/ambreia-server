using System;

namespace Vortex.Dashboard.API.Api.Platform.Contracts;

/// <summary>
/// Which room the timeline belongs to.
/// </summary>
/// <param name="OwnerPlayerId">The same value as <paramref name="RoomOwnerId"/>. Both were on the
/// wire before this record existed, and the surfaces reading it are split between the two names.
/// </param>
public sealed record RoomTimelineHeader(
    int RoomId,
    string Name,
    string? Description,
    int RoomOwnerId,
    string? RoomOwnerName,
    int OwnerPlayerId,
    int UsersNow,
    int PlayersMax,
    DateTime LastActive,
    string ModelName
);
