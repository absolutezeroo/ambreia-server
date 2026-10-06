using Orleans;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Orleans.Snapshots.Room;

namespace Vortex.Protocol.Messages.Outgoing.Navigator;

[GenerateSerializer, Immutable]
public sealed record GetGuestRoomResultMessageComposer : IComposer
{
    [Id(0)]
    public required bool EnterRoom { get; init; }

    [Id(1)]
    public required RoomSnapshot RoomInfo { get; init; }

    [Id(2)]
    public required bool RoomForward { get; init; }

    // Id 3 was StaffPick; the wire field is now always false (see the serializer).

    [Id(4)]
    public required bool IsGroupMember { get; init; }

    [Id(5)]
    public required bool AllInRoomMuted { get; init; }

    [Id(6)]
    public required bool CanMute { get; init; }

    [Id(7)]
    public required bool OpeningConnection { get; init; }
}
