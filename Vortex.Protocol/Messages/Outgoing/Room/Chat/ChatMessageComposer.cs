using System.Collections.Generic;
using Orleans;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;

namespace Vortex.Protocol.Messages.Outgoing.Room.Chat;

[GenerateSerializer, Immutable]
public record ChatMessageComposer : IComposer
{
    [Id(0)]
    public required RoomObjectId ObjectId { get; init; }

    [Id(1)]
    public required string Text { get; init; }

    [Id(2)]
    public required AvatarGestureType Gesture { get; init; }

    // [Id(3)] was StyleId, retired with chat styles (M0 bis): never reuse it. The wire field is
    // written as 0 by the serializers.

    [Id(4)]
    public required List<(string, string, bool)> Links { get; init; }

    [Id(5)]
    public required int TrackingId { get; init; }
}
