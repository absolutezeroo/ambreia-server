using Orleans;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Pets.Snapshots;

namespace Vortex.Protocol.Messages.Outgoing.Users;

[GenerateSerializer, Immutable]
public sealed record PetRespectNotificationEventMessageComposer : IComposer
{
    [Id(0)]
    public required int PetRespect { get; init; }

    [Id(1)]
    public required int PetOwnerId { get; init; }

    /// <summary>
    /// The pet block the client reads after the two counters is the same one it reads everywhere a
    /// pet is drawn, so the snapshot travels whole rather than being spread over flat fields. It
    /// used to be: the palette id was one of them, nobody set it, and every pet was announced in
    /// breed 1's colours.
    /// </summary>
    [Id(2)]
    public required PetSnapshot Pet { get; init; }
}
