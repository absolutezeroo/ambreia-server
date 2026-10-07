using Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.Server.Grains;

namespace Vortex.Primitives.Orleans;

public static class GrainFactoryExtensions
{
    /// <summary>The whole room surface. Prefer one of the <c>GetRoom*</c> facet accessors below;
    /// this is for the few call sites that genuinely span most of the room.</summary>
    public static IRoomGrain GetRoomGrain(this IGrainFactory factory, RoomId roomId) =>
        factory.GetGrain<IRoomGrain>((long)roomId.Value);

    // Facets of the room grain. Every one of these resolves to the same RoomGrain activation as
    // GetRoomGrain for the same room id -- RoomGrain is the only implementation of all of them, so
    // asking for a facet is not a different grain, just a narrower view of the same one. Take the
    // narrowest facet a call site actually needs: it makes the dependency legible and keeps the
    // blast radius of a room-interface change proportional to what really uses it.

    public static IRoomCore GetRoomCore(this IGrainFactory factory, RoomId roomId) =>
        factory.GetGrain<IRoomCore>((long)roomId.Value);

    public static IRoomAvatars GetRoomAvatars(this IGrainFactory factory, RoomId roomId) =>
        factory.GetGrain<IRoomAvatars>((long)roomId.Value);

    public static IRoomMap GetRoomMap(this IGrainFactory factory, RoomId roomId) =>
        factory.GetGrain<IRoomMap>((long)roomId.Value);

    public static IRoomFurni GetRoomFurni(this IGrainFactory factory, RoomId roomId) =>
        factory.GetGrain<IRoomFurni>((long)roomId.Value);

    public static IRoomSecurity GetRoomSecurity(this IGrainFactory factory, RoomId roomId) =>
        factory.GetGrain<IRoomSecurity>((long)roomId.Value);

    public static IRoomPersistenceGrain GetRoomPersistenceGrain(
        this IGrainFactory factory,
        RoomId roomId
    ) => factory.GetGrain<IRoomPersistenceGrain>((long)roomId.Value);

    public static IRoomDirectoryGrain GetRoomDirectoryGrain(this IGrainFactory factory) =>
        factory.GetGrain<IRoomDirectoryGrain>(SingletonGrainId.GLOBAL);

    public static IPlayerGrain GetPlayerGrain(this IGrainFactory factory, PlayerId playerId) =>
        factory.GetGrain<IPlayerGrain>((long)playerId.Value);

    public static IPlayerPresenceGrain GetPlayerPresenceGrain(
        this IGrainFactory factory,
        PlayerId playerId
    ) => factory.GetGrain<IPlayerPresenceGrain>(playerId.Value);

    public static IPlayerPresenceGrain GetPlayerPresenceGrain(
        this IGrainFactory factory,
        long playerId
    ) => factory.GetGrain<IPlayerPresenceGrain>(playerId);

    public static IPlayerDirectoryGrain GetPlayerDirectoryGrain(this IGrainFactory factory) =>
        factory.GetGrain<IPlayerDirectoryGrain>(SingletonGrainId.GLOBAL);

    public static IServerConfigGrain GetServerConfigGrain(this IGrainFactory factory) =>
        factory.GetGrain<IServerConfigGrain>(SingletonGrainId.GLOBAL);
}
