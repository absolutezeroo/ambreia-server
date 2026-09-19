using Orleans;

namespace Vortex.Primitives.Camera;

/// <summary>
/// What came of a <c>PublishPhoto</c>: the share id, or how long until they may try again.
/// </summary>
/// <remarks>
/// The client has no third state. A refusal always turns into "wait N minutes", so a refusal for any
/// other reason — no photo taken, already published — still has to name a number of seconds, and
/// zero reads as "wait 1 minute" rather than as an error. Whatever the reason, the honest number is
/// the cooldown.
/// </remarks>
[GenerateSerializer, Immutable]
public sealed record CameraPublishOutcome
{
    [Id(0)]
    public required bool Ok { get; init; }

    [Id(1)]
    public required int SecondsToWait { get; init; }

    [Id(2)]
    public required string? ExtraDataId { get; init; }

    public static CameraPublishOutcome Published(string extraDataId) =>
        new()
        {
            Ok = true,
            SecondsToWait = 0,
            ExtraDataId = extraDataId,
        };

    public static CameraPublishOutcome Wait(int secondsToWait) =>
        new()
        {
            Ok = false,
            SecondsToWait = secondsToWait,
            ExtraDataId = null,
        };
}
