using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Roomsettings;

/// <summary>
/// One lifted room ban (header 2089).
///
/// The only thing that takes a row out of the room settings ban list:
/// <c>RoomSettingsCtrl.onUserUnbannedFromRoom</c> calls <c>setBannedUser(userId, null)</c> and
/// refreshes. Without it the ban is lifted on the server and the operator still sees the name,
/// clicks unban again, and gets nothing.
/// </summary>
[GenerateSerializer, Immutable]
public sealed record UserUnbannedFromRoomEventMessageComposer : IComposer
{
    /// <summary>
    /// The room the ban was held in. The client drops the message unless this matches the room
    /// whose settings are open (<c>isAcceptBannedUsersFromRoomUpdate</c>).
    /// </summary>
    [Id(0)]
    public required int RoomId { get; init; }

    [Id(1)]
    public required int PlayerId { get; init; }
}
