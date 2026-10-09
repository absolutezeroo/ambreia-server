using Vortex.Primitives.Rooms.Object.Logic.Avatars;

namespace Vortex.Primitives.Rooms.Object.Avatars;

public interface IRoomMonsterContext
    : IRoomAvatarContext<IRoomMonster, IRoomMonsterLogic, IRoomMonsterContext>
{
    new IRoomMonster RoomObject { get; }
}
