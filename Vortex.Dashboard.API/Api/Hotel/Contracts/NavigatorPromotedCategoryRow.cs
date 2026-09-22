namespace Vortex.Dashboard.API.Api.Hotel.Contracts;

/// <summary>
/// One promoted-rooms group, with how many rooms its search currently finds.
/// </summary>
/// <param name="RoomCount">What the group would carry right now. Zero is the one number that
/// matters: a group whose search finds nothing is not sent to the client at all, so an operator who
/// cannot see this count has no way to tell a configured group from an invisible one.</param>
public sealed record NavigatorPromotedCategoryRow(
    int Id,
    string Code,
    string ClientText,
    string SearchCode,
    string Filter,
    int OrderNum,
    bool Visible,
    int RoomCount
);
