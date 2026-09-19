using System.Collections.Immutable;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Incoming.Marketplace;

/// <summary>
/// One marketplace listing request covering <see cref="FurnitureItemIds"/> — the client lists a
/// whole selection at one price in a single message, so this is an array and not a single id.
/// The ids are the inventory strip's <c>ref</c> field, which is signed by item kind on the wire;
/// the handler normalises them.
/// </summary>
public record MakeOfferMessage : IMessageEvent
{
    public int Price { get; init; }
    public int FurniType { get; init; }
    public required ImmutableArray<int> FurnitureItemIds { get; init; }
}
