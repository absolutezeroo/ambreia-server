using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Camera;

/// <summary>What the room camera charges, answered to <c>RequestCameraConfiguration</c>.</summary>
/// <remarks>
/// Three ints, read straight into the camera widget by
/// <c>CameraWidgetHandler.onInitCameraEvent</c> and set by nothing else. The record carried no
/// properties at all and its serializer wrote an empty body, so the widget kept its own defaults and
/// the hotel's prices never reached it.
/// </remarks>
[GenerateSerializer, Immutable]
public sealed record InitCameraMessageComposer : IComposer
{
    [Id(0)]
    public required int CreditPrice { get; init; }

    [Id(1)]
    public required int DucketPrice { get; init; }

    /// <summary>Charged to publish to the hotel's feed, on top of taking the photo.</summary>
    [Id(2)]
    public required int PublishDucketPrice { get; init; }
}
