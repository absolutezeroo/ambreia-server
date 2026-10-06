using Vortex.Primitives.Rooms.Grains;

namespace Vortex.Rooms.Grains;

/// <summary>
/// Declares every room facet <see cref="RoomGrain"/> implements.
/// <para>
/// <see cref="IRoomGrain"/> already derives from all of them, so this list is redundant to the
/// compiler. It is spelled out anyway because it is the only place that states, in one view, that a
/// single <c>RoomGrain</c> activation is what every facet resolves to — one implementation per facet
/// is precisely what keeps a facet reference pointing at the same activation as the aggregate.
/// </para>
/// <para>
/// The members live in the per-domain <c>RoomGrain.*</c> parts; nothing is implemented here.
/// </para>
/// </summary>
public sealed partial class RoomGrain
    : IRoomCore,
        IRoomAvatars,
        IRoomMap,
        IRoomFurni,
        IRoomPets,
        IRoomSecurity,
        IRoomSettings,
        IRoomModeration,
        IRoomTrading,
        IRoomMysteryBox,
        IRoomDoorbell,
        IRoomCrackable { }
