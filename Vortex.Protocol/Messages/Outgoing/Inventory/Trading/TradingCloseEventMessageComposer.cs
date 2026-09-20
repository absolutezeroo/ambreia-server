using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Inventory.Trading;

[GenerateSerializer, Immutable]
public sealed record TradingCloseEventMessageComposer : IComposer
{
    /// <summary>Player (web) id of the user who caused the close (the canceller) -- the same id
    /// space as <see cref="TradingOpenEventMessageComposer.UserId"/>, which explains why. The
    /// client shows "the other user cancelled" when this differs from the id it was opened
    /// with.</summary>
    [Id(0)]
    public required int UserId { get; init; }

    /// <summary>Close reason. <c>1</c> = commit/server error (special alert); any other value = a
    /// user cancelled the trade.</summary>
    [Id(1)]
    public required int Reason { get; init; }
}
