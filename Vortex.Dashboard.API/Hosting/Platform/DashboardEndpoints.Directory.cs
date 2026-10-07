using System;
using System.Collections.Generic;
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
using Vortex.Dashboard.API.Api.Platform;
using Vortex.Dashboard.API.Api.Platform.Contracts;
using Vortex.Dashboard.API.Infrastructure;
using Vortex.Dashboard.API.Operations;
using Vortex.Dashboard.API.Security;
using Vortex.Primitives.Permissions;

namespace Vortex.Dashboard.API.Hosting;

internal static partial class DashboardEndpoints
{
    public static void MapDirectoryReads(WebApplication app)
    {
        MapReadGet<DirectorySearch>(
            app,
            ApiDirectory + "/search",
            (HttpContext ctx, DirectoryReads reads, CancellationToken ct) =>
                OkAsync(reads.SearchAsync(ctx.QueryAsNameValues(), ct)),
            Capabilities.Dashboard.AuditRead,
            TagForensics
        );
        MapReadGet<PlayerDirectoryPage>(
            app,
            ApiDirectory + "/players",
            (HttpContext ctx, DirectoryReads reads, CancellationToken ct) =>
                OkAsync(reads.PlayersAsync(ctx.QueryAsNameValues(), ct)),
            Capabilities.Dashboard.PlayersRead,
            TagDirectory
        );
        // Batch avatar-head resolver (?ids=1,2,3) so every surface that shows a player can render the
        // real head via lib/avatars.js. Users without PlayersRead simply keep the plain name.
        MapReadGet<AvatarBatch>(
            app,
            ApiDirectory + "/avatars",
            (HttpContext ctx, DirectoryReads reads, CancellationToken ct) =>
                OkAsync(reads.AvatarsAsync(ctx.QueryAsNameValues(), ct)),
            Capabilities.Dashboard.PlayersRead,
            TagDirectory
        );
        // Room search for the shared picker, so a surface that pins something to a room hands back
        // an id the operator picked rather than typed.
        MapReadGet<RoomDirectoryPage>(
            app,
            ApiDirectory + "/rooms",
            (HttpContext ctx, DirectoryReads reads, CancellationToken ct) =>
                OkAsync(reads.RoomsDirectoryAsync(ctx.QueryAsNameValues(), ct)),
            Capabilities.Dashboard.AuditRead,
            TagDirectory
        );
        MapReadGet<FurnitureDirectoryPage>(
            app,
            ApiDirectory + "/furniture",
            (HttpContext ctx, DirectoryReads reads, CancellationToken ct) =>
                OkAsync(reads.FurnitureDefinitionsAsync(ctx.QueryAsNameValues(), ct)),
            Capabilities.Dashboard.FurnitureRead,
            TagDirectory
        );
        // The directories behind the reward-track filter pickers. Each replaces an id or a code an
        // operator was expected to type from memory into a filter that saves cleanly and then never
        // matches -- the failure this subsystem exists to prevent, reappearing at the last step.
        MapReadGet<DirectoryPage>(
            app,
            ApiDirectory + "/groups",
            (HttpContext ctx, SignalDirectoryReads signalDirectory, CancellationToken ct) =>
                OkAsync(signalDirectory.GroupsDirectoryAsync(ctx.QueryAsNameValues(), ct)),
            Capabilities.Dashboard.SocialRead,
            TagDirectory
        );
        MapReadGet<DirectoryPage>(
            app,
            ApiDirectory + "/catalog-offers",
            (HttpContext ctx, SignalDirectoryReads signalDirectory, CancellationToken ct) =>
                OkAsync(signalDirectory.CatalogOffersDirectoryAsync(ctx.QueryAsNameValues(), ct)),
            Capabilities.Dashboard.CatalogRead,
            TagDirectory
        );
        MapReadGet<DirectoryPage>(
            app,
            ApiDirectory + "/navigator-categories",
            (HttpContext ctx, SignalDirectoryReads signalDirectory, CancellationToken ct) =>
                OkAsync(
                    signalDirectory.NavigatorCategoriesDirectoryAsync(ctx.QueryAsNameValues(), ct)
                ),
            Capabilities.Dashboard.NavigatorRead,
            TagDirectory
        );
        MapReadGet<CodeDirectoryPage>(
            app,
            ApiDirectory + "/badges",
            (HttpContext ctx, SignalDirectoryReads signalDirectory, CancellationToken ct) =>
                OkAsync(signalDirectory.BadgesDirectoryAsync(ctx.QueryAsNameValues(), ct)),
            Capabilities.Dashboard.PlayersRead,
            TagDirectory
        );
        // Garments, from figuredata rather than a table -- it is the file the client is served, and
        // a copy in the database would go stale the day the asset pack is swapped.
        MapReadGet<FigureSetDirectoryPage>(
            app,
            ApiDirectory + "/figure-sets",
            (HttpContext ctx, SignalDirectoryReads signalDirectory, CancellationToken ct) =>
                OkAsync(signalDirectory.FigureSetsDirectoryAsync(ctx.QueryAsNameValues())),
            Capabilities.Dashboard.PlayersRead,
            TagDirectory
        );
        MapReadGet<PetDirectoryPage>(
            app,
            ApiDirectory + "/pet-species",
            (HttpContext ctx, SignalDirectoryReads signalDirectory, CancellationToken ct) =>
                OkAsync(signalDirectory.PetSpeciesDirectoryAsync(ctx.QueryAsNameValues(), ct)),
            Capabilities.Dashboard.PetsRead,
            TagDirectory
        );
        MapReadGet<CodeDirectoryPage>(
            app,
            ApiDirectory + "/quest-campaigns",
            (HttpContext ctx, SignalDirectoryReads signalDirectory, CancellationToken ct) =>
                OkAsync(signalDirectory.QuestCampaignsDirectoryAsync(ctx.QueryAsNameValues(), ct)),
            Capabilities.Dashboard.QuestsRead,
            TagDirectory
        );
        MapReadGet<DirectoryPage>(
            app,
            ApiDirectory + "/club-gifts",
            (HttpContext ctx, SignalDirectoryReads signalDirectory, CancellationToken ct) =>
                OkAsync(signalDirectory.ClubGiftsDirectoryAsync(ctx.QueryAsNameValues(), ct)),
            Capabilities.Dashboard.CatalogRead,
            TagDirectory
        );
        MapReadGet<DirectoryPage>(
            app,
            ApiDirectory + "/forum-threads",
            (HttpContext ctx, SignalDirectoryReads signalDirectory, CancellationToken ct) =>
                OkAsync(signalDirectory.ForumThreadsDirectoryAsync(ctx.QueryAsNameValues(), ct)),
            Capabilities.Dashboard.SocialRead,
            TagDirectory
        );
        MapReadGet<DirectoryPage>(
            app,
            ApiDirectory + "/avatar-effects",
            (HttpContext ctx, SignalDirectoryReads signalDirectory, CancellationToken ct) =>
                OkAsync(signalDirectory.AvatarEffectsDirectoryAsync(ctx.QueryAsNameValues(), ct)),
            Capabilities.Dashboard.PlayersRead,
            TagDirectory
        );
        MapReadGet<DirectoryPage>(
            app,
            ApiDirectory + "/placed-furniture",
            (HttpContext ctx, SignalDirectoryReads signalDirectory, CancellationToken ct) =>
                OkAsync(signalDirectory.PlacedFurnitureDirectoryAsync(ctx.QueryAsNameValues(), ct)),
            Capabilities.Dashboard.FurnitureRead,
            TagDirectory
        );
        MapReadGetNullable<ItemProfile>(
            app,
            ApiDirectory + "/entity/{id}",
            (string id, HttpContext ctx, DirectoryReads reads, CancellationToken ct) =>
                OkNullableAsync(reads.ItemAsync(id, ctx.QueryAsNameValues(), ct)),
            Capabilities.Dashboard.AuditRead,
            TagForensics
        );
        // The profile alone, for the popup that opens on any player id in the dashboard. It used to
        // call /search, which also assembles the audit trail, the ledger, the chat and the item
        // history — a dozen queries it never reads. Same capability as /search deliberately: the
        // profile carries the player's recent chat, so serving it under PlayersRead would widen who
        // can read chatlogs.
        MapReadGetNullable<PlayerProfile>(
            app,
            ApiDirectory + "/players/{playerId:int}/profile",
            (int playerId, HttpContext ctx, DirectoryReads reads, CancellationToken ct) =>
                OkNullableAsync(reads.PlayerProfileAsync(playerId, ctx.QueryAsNameValues(), ct)),
            Capabilities.Dashboard.AuditRead,
            TagForensics
        );
        MapReadGetNullable<RoomTimeline>(
            app,
            ApiDirectory + "/rooms/{roomId:int}",
            (int roomId, HttpContext ctx, DirectoryReads reads, CancellationToken ct) =>
                OkNullableAsync(reads.RoomTimelineAsync(roomId, ctx.QueryAsNameValues(), ct)),
            Capabilities.Dashboard.AuditRead,
            TagForensics
        );
    }
}
