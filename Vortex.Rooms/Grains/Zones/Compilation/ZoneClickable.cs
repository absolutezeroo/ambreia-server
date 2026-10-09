namespace Vortex.Rooms.Grains.Zones.Compilation;

/// <summary>A decor object a player can click (spec F3b §5): its room object id and what clicking
/// it does — an interaction (<c>door</c>, <c>sign</c>…) or <c>interact</c> for an Interact trigger.</summary>
public sealed record ZoneClickable(int ObjectId, string Interaction);
