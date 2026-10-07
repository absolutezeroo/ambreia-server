using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Dashboard.API.Api.Catalogue.Contracts;
using Vortex.Database.Context;

namespace Vortex.Dashboard.API.Api.Catalogue;

/// <summary>
/// One row per subsystem: how much of it actually exists. The Overview answers "is the hotel
/// healthy right now"; this answers the other question an operator has — "what is in it at all" —
/// which until now meant opening a dozen pages, or knowing which tables to read.
/// <para>
/// A zero here is the point of the panel: a subsystem that is fully built server-side and holds no
/// rows is either unseeded or unreachable, and neither shows up anywhere else.
/// </para>
/// </summary>
internal sealed class InventoryReads(IDbContextFactory<VortexDbContext> dbContextFactory)
    : DashboardReads(dbContextFactory)
{
    public Task<DashboardInventory> InventoryAsync(CancellationToken ct) =>
        QueryAsync<DashboardInventory>(
            async db =>
            {
                DateTime now = DateTime.UtcNow;

                // Counted in one pass and shaped as {domain, label, count, route} so the panel is a
                // plain list the front end does not have to keep in sync with this file.
                List<InventoryGroup> groups =
                [
                    Group(
                        "world",
                        [
                            Row(
                                "rooms",
                                await db
                                    .Rooms.CountAsync(r => r.DeletedAt == null, ct)
                                    .ConfigureAwait(false),
                                "/rooms"
                            ),
                            Row(
                                "roomModels",
                                await db.RoomModels.CountAsync(ct).ConfigureAwait(false),
                                null
                            ),
                            Row(
                                "furniture",
                                await db
                                    .Furnitures.CountAsync(f => f.DeletedAt == null, ct)
                                    .ConfigureAwait(false),
                                null
                            ),
                            Row(
                                "furnitureDefinitions",
                                await db.FurnitureDefinitions.CountAsync(ct).ConfigureAwait(false),
                                "/furniture-definitions"
                            ),
                            Row(
                                "bots",
                                await db
                                    .Bots.CountAsync(b => b.DeletedAt == null, ct)
                                    .ConfigureAwait(false),
                                "/bots"
                            ),
                            Row(
                                "pets",
                                await db
                                    .Pets.CountAsync(p => p.DeletedAt == null, ct)
                                    .ConfigureAwait(false),
                                "/pets-stats"
                            ),
                        ]
                    ),
                    Group(
                        "players",
                        [
                            Row(
                                "players",
                                await db
                                    .Players.CountAsync(p => p.DeletedAt == null, ct)
                                    .ConfigureAwait(false),
                                "/investigation"
                            ),
                            Row(
                                "accounts",
                                await db
                                    .PlayerAccounts.CountAsync(a => a.DeletedAt == null, ct)
                                    .ConfigureAwait(false),
                                "/staff"
                            ),
                            Row(
                                "badges",
                                await db
                                    .PlayerBadges.CountAsync(b => b.DeletedAt == null, ct)
                                    .ConfigureAwait(false),
                                "/player-rewards"
                            ),
                            Row(
                                "effects",
                                await db
                                    .PlayerEffects.CountAsync(e => e.DeletedAt == null, ct)
                                    .ConfigureAwait(false),
                                "/player-rewards"
                            ),
                            Row(
                                "wardrobeOutfits",
                                await db
                                    .PlayerWardrobeOutfits.CountAsync(o => o.DeletedAt == null, ct)
                                    .ConfigureAwait(false),
                                "/player-rewards"
                            ),
                            Row(
                                "subscriptions",
                                await db
                                    .PlayerMemberships.CountAsync(s => s.DeletedAt == null, ct)
                                    .ConfigureAwait(false),
                                "/subscriptions"
                            ),
                        ]
                    ),
                    Group(
                        "progression",
                        [
                            Row(
                                "achievements",
                                await db.Achievements.CountAsync(ct).ConfigureAwait(false),
                                "/achievements"
                            ),
                            Row(
                                "achievementLevels",
                                await db.AchievementLevels.CountAsync(ct).ConfigureAwait(false),
                                "/achievements"
                            ),
                            Row(
                                "quests",
                                await db.Quests.CountAsync(ct).ConfigureAwait(false),
                                "/quests"
                            ),
                            Row(
                                "lootTables",
                                await db
                                    .LootTables.CountAsync(p => p.DeletedAt == null, ct)
                                    .ConfigureAwait(false),
                                "/loot-tables"
                            ),
                        ]
                    ),
                    Group(
                        "commerce",
                        [
                            Row(
                                "catalogPages",
                                await db
                                    .CatalogPages.CountAsync(p => p.DeletedAt == null, ct)
                                    .ConfigureAwait(false),
                                "/catalog"
                            ),
                            Row(
                                "catalogOffers",
                                await db
                                    .CatalogOffers.CountAsync(o => o.DeletedAt == null, ct)
                                    .ConfigureAwait(false),
                                "/catalog"
                            ),
                            Row(
                                "marketplaceOffers",
                                await db
                                    .MarketplaceOffers.CountAsync(o => o.DeletedAt == null, ct)
                                    .ConfigureAwait(false),
                                "/marketplace"
                            ),
                            Row(
                                "currencies",
                                await db
                                    .CurrencyTypes.CountAsync(c => c.DeletedAt == null, ct)
                                    .ConfigureAwait(false),
                                "/economy-extras"
                            ),
                        ]
                    ),
                    Group(
                        "social",
                        [
                            Row(
                                "guilds",
                                await db
                                    .Groups.CountAsync(g => g.DeletedAt == null, ct)
                                    .ConfigureAwait(false),
                                "/groups-stats"
                            ),
                            Row(
                                "forumThreads",
                                await db
                                    .GroupForumThreads.CountAsync(t => t.DeletedAt == null, ct)
                                    .ConfigureAwait(false),
                                "/social"
                            ),
                            Row(
                                "friendships",
                                await db
                                    .MessengerFriends.CountAsync(f => f.DeletedAt == null, ct)
                                    .ConfigureAwait(false) / 2,
                                "/social"
                            ),
                            Row(
                                "privateMessages",
                                await db.MessengerMessages.CountAsync(ct).ConfigureAwait(false),
                                "/social"
                            ),
                        ]
                    ),
                    Group(
                        "operations",
                        [
                            Row(
                                "navigatorTabs",
                                await db
                                    .NavigatorTopLevelContexts.CountAsync(
                                        c => c.DeletedAt == null,
                                        ct
                                    )
                                    .ConfigureAwait(false),
                                "/navigator-config"
                            ),
                            Row(
                                "navigatorCategories",
                                await db
                                    .NavigatorFlatCategories.CountAsync(
                                        c => c.DeletedAt == null,
                                        ct
                                    )
                                    .ConfigureAwait(false),
                                "/navigator-config"
                            ),
                            Row(
                                "roles",
                                await db
                                    .Roles.CountAsync(r => r.DeletedAt == null, ct)
                                    .ConfigureAwait(false),
                                "/staff"
                            ),
                            Row(
                                "sanctionPresets",
                                await db
                                    .SanctionPresets.CountAsync(p => p.DeletedAt == null, ct)
                                    .ConfigureAwait(false),
                                "/staff"
                            ),
                            Row(
                                "openCfhTickets",
                                await db
                                    .CfhTickets.CountAsync(
                                        t => t.DeletedAt == null && t.ClosedAt == null,
                                        ct
                                    )
                                    .ConfigureAwait(false),
                                "/cfh"
                            ),
                            Row(
                                "activeBans",
                                await db
                                    .AccountBans.CountAsync(
                                        b => b.DeletedAt == null && b.DateExpires > now,
                                        ct
                                    )
                                    .ConfigureAwait(false),
                                "/moderation"
                            ),
                            Row(
                                "serverConfigKeys",
                                await db.ServerConfig.CountAsync(ct).ConfigureAwait(false),
                                "/config"
                            ),
                        ]
                    ),
                ];

                return new DashboardInventory(now, groups);
            },
            ct
        );

    private static InventoryGroup Group(string key, List<InventoryRow> rows) => new(key, rows);

    private static InventoryRow Row(string key, int count, string? route) =>
        new(key, count, route, count == 0);
}
