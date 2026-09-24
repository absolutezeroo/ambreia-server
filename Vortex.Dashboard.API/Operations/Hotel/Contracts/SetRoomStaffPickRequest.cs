using Vortex.Dashboard.API.Hosting;

namespace Vortex.Dashboard.API.Operations.Hotel.Contracts;

/// <summary>
/// Put a room in, or take it out of, the hotel's staff picks.
/// </summary>
/// <remarks>
/// The flag feeds the public rooms view, the <c>official</c> and <c>staffpicks</c> blocks, the badge
/// on the room card and half of the "recommended" query, and until now the only way to set it was a
/// button the in-client navigator shows to an administrator. An operator had no way at all.
/// </remarks>
public sealed record SetRoomStaffPickRequest(int RoomId, bool StaffPick, string Reason)
    : IReasonedRequest;
