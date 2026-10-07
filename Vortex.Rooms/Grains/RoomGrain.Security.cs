using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Primitives.Action;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;

namespace Vortex.Rooms.Grains;

public sealed partial class RoomGrain
{
    public Task<RoomControllerType> GetControllerLevelAsync(
        ActionContext ctx,
        CancellationToken ct
    ) => SecurityModule.GetControllerLevelAsync(ctx);
}
