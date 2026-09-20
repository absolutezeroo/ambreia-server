using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Inventory.Trading;

[GenerateSerializer, Immutable]
public sealed record TradingOpenEventMessageComposer : IComposer
{
    /// <summary>Player (web) id of the trade initiator, and the id space every server-to-client
    /// trade message uses.
    /// <para>
    /// Not the room-object id, although <c>OpenTrading</c> does arrive as one. The client answers
    /// this with <c>userDataManager.getUserData()</c>, keyed by web id (<c>_userDataByType</c>);
    /// the room-index map is the separate <c>getUserDataByIndex()</c>. It then decides which half
    /// of the window is yours with <c>otherUserId == sessionDataManager.userId</c>, a web id again.
    /// Sending object ids matched only while the room's per-room object counter happened to hand
    /// out the same numbers as the player ids -- true in a fresh two-player hotel, false as soon
    /// as a third avatar has entered -- and past that point <c>getUserData()</c> returns null and
    /// the trade window never opens at all.
    /// </para></summary>
    [Id(0)]
    public required int UserId { get; init; }

    [Id(1)]
    public required bool UserCanTrade { get; init; }

    [Id(2)]
    public required int OtherUserId { get; init; }

    [Id(3)]
    public required bool OtherUserCanTrade { get; init; }
}
