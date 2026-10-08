using Vortex.Primitives.Rooms.Object.Logic;

namespace Vortex.Primitives.Rooms.Object;

public interface IMutableRoomObject<TSelf, TLogic, TContext> : IRoomObject<TSelf, TLogic, TContext>
    where TSelf : IRoomObject<TSelf, TLogic, TContext>
    where TContext : IRoomObjectContext<TSelf, TLogic, TContext>
    where TLogic : IRoomObjectLogic<TSelf, TLogic, TContext>
{
    void SetLogic(TLogic logic);
}
