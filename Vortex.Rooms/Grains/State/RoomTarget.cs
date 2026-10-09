using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Object;

namespace Vortex.Rooms.Grains.State;

/// <summary>What a player aims at (spec F3b §6): the avatar's room object id, and the player behind
/// it — kept so a departure can clear the target after the avatar is gone.</summary>
public readonly record struct RoomTarget(RoomObjectId ObjectId, PlayerId PlayerId);
