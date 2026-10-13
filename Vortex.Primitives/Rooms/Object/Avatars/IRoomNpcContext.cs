using Vortex.Primitives.Rooms.Object.Logic.Avatars;

namespace Vortex.Primitives.Rooms.Object.Avatars;

public interface IRoomNpcContext : IRoomAvatarContext<IRoomNpc, IRoomNpcLogic, IRoomNpcContext>
{
    new IRoomNpc RoomObject { get; }
}
