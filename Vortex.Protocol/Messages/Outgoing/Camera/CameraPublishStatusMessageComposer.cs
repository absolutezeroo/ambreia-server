using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Camera;

/// <summary>
/// The answer to <c>PublishPhoto</c>.
/// </summary>
/// <remarks>
/// Three fields, and the third is conditional: the client reads <c>ok</c>, then
/// <c>secondsToWait</c>, and only reaches for the id when <c>ok</c> is true <em>and</em> bytes
/// remain. On the failure branch it turns the seconds into "wait N minutes" and says nothing else,
/// so a refusal with zero seconds reads to the player as "wait 1 minute" rather than as an error.
/// </remarks>
[GenerateSerializer, Immutable]
public sealed record CameraPublishStatusMessageComposer : IComposer
{
    [Id(0)]
    public required bool Ok { get; init; }

    /// <summary>How long until they may publish again. Only read when <see cref="Ok"/> is false.</summary>
    [Id(1)]
    public required int SecondsToWait { get; init; }

    /// <summary>
    /// The published photo's own id, which the client turns into the share link it then reveals.
    /// Null on a refusal, and omitted from the wire entirely — the client's optional read is what
    /// makes that legal.
    /// </summary>
    [Id(2)]
    public required string? ExtraDataId { get; init; }
}
