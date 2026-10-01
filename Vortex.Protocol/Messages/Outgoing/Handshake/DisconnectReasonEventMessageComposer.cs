using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Handshake;

/// <summary>
/// Why the server is about to drop the connection. The client turns the code into the text of its
/// disconnect screen (<c>DisconnectReasonEvent.resolveDisconnectedReasonLocalizationKey</c>).
/// </summary>
[GenerateSerializer, Immutable]
public sealed record DisconnectReasonEventMessageComposer : IComposer
{
    /// <summary>The account logged in somewhere else: <c>${disconnected.concurrent_login}</c>.</summary>
    public const int ConcurrentLogin = 2;

    [Id(0)]
    public required int Reason { get; init; }
}
