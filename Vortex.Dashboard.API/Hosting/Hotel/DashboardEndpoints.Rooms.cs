using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Vortex.Dashboard.API.Api;
using Vortex.Dashboard.API.Infrastructure;
using Vortex.Dashboard.API.Operations;
using Vortex.Dashboard.API.Operations.Hotel;
using Vortex.Dashboard.API.Operations.Hotel.Contracts;
using Vortex.Dashboard.API.Security;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Permissions;

namespace Vortex.Dashboard.API.Hosting;

internal static partial class DashboardEndpoints
{
    public static void MapRoomReads(WebApplication app)
    {
        MapReadGet<ImmutableArray<RoomSummaryDto>>(
            app,
            ApiDirectory + "/rooms/active",
            async (RoomOperations ops, CancellationToken ct) =>
                Results.Ok(await ops.GetActiveRoomsAsync().ConfigureAwait(false)),
            Capabilities.Dashboard.OpsRoomsManage,
            TagDirectory
        );
        MapReadGet<ImmutableArray<RoomOccupantSnapshot>>(
            app,
            ApiDirectory + "/rooms/{roomId:int}/occupants",
            async (int roomId, RoomOperations ops, CancellationToken ct) =>
                Results.Ok(await ops.GetRoomOccupantsAsync(roomId, ct).ConfigureAwait(false)),
            Capabilities.Dashboard.OpsRoomsManage,
            TagDirectory
        );
    }

    public static void MapRoomOperations(WebApplication app)
    {
        // Guarded by the item capability rather than the room one: this moves somebody's property,
        // and it exists so the revoke's item_is_placed refusal has a way forward.
        MapPost(
            app,
            ApiOperations + "/items/pickup",
            async (
                HttpContext ctx,
                PickUpFurnitureRequest body,
                RoomOperations ops,
                CancellationToken ct
            ) =>
                body.RoomId <= 0 || body.ItemId <= 0
                    ? Results.BadRequest(new { error = "invalid_request" })
                    : Results.Ok(
                        await ops.PickUpFurnitureAsync(body, ctx.ActorEmail(), ct)
                            .ConfigureAwait(false)
                    ),
            Capabilities.Dashboard.OpsGrantItem,
            TagOperations
        );
        MapPost(
            app,
            ApiOperations + "/rooms/close",
            async (
                HttpContext ctx,
                ForceCloseRoomRequest body,
                RoomOperations ops,
                CancellationToken ct
            ) =>
            {
                if (body.RoomId <= 0)
                {
                    return Results.BadRequest(new { error = "invalid_request" });
                }

                return Results.Ok(
                    await ops.ForceCloseRoomAsync(body, ctx.ActorEmail(), ct).ConfigureAwait(false)
                );
            },
            Capabilities.Dashboard.OpsRoomsManage,
            TagOperations
        );
        MapPost(
            app,
            ApiOperations + "/rooms/kick",
            async (
                HttpContext ctx,
                KickFromRoomRequest body,
                RoomOperations ops,
                CancellationToken ct
            ) =>
            {
                if (body.RoomId <= 0 || body.PlayerId <= 0)
                {
                    return Results.BadRequest(new { error = "invalid_request" });
                }

                return Results.Ok(
                    await ops.KickFromRoomAsync(body, ctx.ActorEmail(), ct).ConfigureAwait(false)
                );
            },
            Capabilities.Dashboard.OpsRoomsManage,
            TagOperations
        );
    }
}
