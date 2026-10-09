using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Incoming.Rpg.Items;

/// <summary>Brings a parcel, by its number, into the bag (spec Objets §17, 9009).</summary>
public record RpgItemClaimParcelMessage : IMessageEvent
{
    public required int Parcel { get; init; }
}
