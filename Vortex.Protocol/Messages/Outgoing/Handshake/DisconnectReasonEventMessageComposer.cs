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

    /// <summary>
    /// The client announced a revision this server does not speak:
    /// <c>${disconnected.incompatible_client_version}</c>. Habbo's own code for it, already known to
    /// the client.
    /// </summary>
    public const int IncompatibleClientVersion = 122;

    [Id(0)]
    public required int Reason { get; init; }
}
