using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Object.Logic.Avatars;

namespace Vortex.Rooms.Object.Logic.Avatar;

public abstract class AvatarLogic<TObject, TSelf, TContext>(TContext ctx)
    : RoomObjectLogic<TObject, TSelf, TContext>(ctx),
        IRoomAvatarLogic<TObject, TSelf, TContext>
    where TObject : IRoomAvatar<TObject, TSelf, TContext>
    where TContext : IRoomAvatarContext<TObject, TSelf, TContext>
    where TSelf : IRoomAvatarLogic<TObject, TSelf, TContext> { }
