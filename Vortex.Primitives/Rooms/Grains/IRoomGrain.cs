using Orleans;

namespace Vortex.Primitives.Rooms.Grains;

/// <summary>
/// The full room-grain surface, as the aggregate of every room facet.
/// <para>
/// Each facet (<see cref="IRoomCore"/>, <see cref="IRoomAvatars"/>, ...) is a grain interface in its
/// own right and is implemented by the same <c>RoomGrain</c> class, so Orleans resolves every one of
/// them to the same activation for a given room id. Requesting a facet is therefore free: it is the
/// same grain, reached through a narrower contract.
/// </para>
/// <para>
/// Prefer depending on the narrowest facet a call site actually needs. This aggregate exists so that
/// existing callers keep compiling unchanged and so that <c>GetRoomGrain</c> can keep handing out one
/// reference that can do everything.
/// </para>
/// </summary>
[Alias("Vortex.Primitives.Rooms.Grains.IRoomGrain")]
public interface IRoomGrain
    : IRoomCore,
        IRoomAvatars,
        IRoomMap,
        IRoomFurni,
        IRoomSecurity,
        IRoomZone,
        IRoomTargets { }
