using Vortex.Primitives.Networking;
using Vortex.Primitives.Orleans.Snapshots.Players;

namespace Vortex.Protocol.Messages.Outgoing.Handshake;

public sealed record UserObjectMessage : IComposer
{
    public required PlayerSummarySnapshot Player { get; init; }

    /// <summary>
    /// The respect budgets. They ride on this message alone: <c>SessionDataManager.onUserObject</c>
    /// is the only writer of <c>respectLeft</c> and <c>petRespectLeft</c>, and the infostand draws
    /// its respect and scratch buttons only while those are above zero.
    /// </summary>
    public required PlayerRespectSnapshot Respect { get; init; }
}
