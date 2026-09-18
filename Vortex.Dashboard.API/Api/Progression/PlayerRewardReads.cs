using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Dashboard.API.Api.Progression.Contracts;
using Vortex.Dashboard.API.Infrastructure;
using Vortex.Database.Context;

namespace Vortex.Dashboard.API.Api.Progression;

/// <summary>
/// Everything a player accumulates that is neither currency nor furniture: badges, avatar effects,
/// chat styles and saved outfits.
/// <para>
/// These tables are only ever read one player at a time by the game, so nothing in the emulator ever
/// looks at them hotel-wide — which is exactly where a broken grant shows up: a badge code held by
/// thousands, an effect nobody ever activated, a chat style owned by nobody because the catalogue
/// never sold it.
/// </para>
/// </summary>
internal sealed class PlayerRewardReads(
    IDbContextFactory<VortexDbContext> dbContextFactory,
    DashboardAssetUrls assetUrls
) : DashboardReads(dbContextFactory)
{
    private readonly DashboardAssetUrls _assetUrls = assetUrls;

    public Task<PlayerRewardStats> PlayerRewardsAsync(
        NameValueCollection query,
        CancellationToken ct
    ) =>
        QueryAsync<PlayerRewardStats>(
            async db =>
            {
                int limit = QueryValues.Limit(query["limit"], 25, 100);

                int totalBadges = await db
                    .PlayerBadges.AsNoTracking()
                    .CountAsync(b => b.DeletedAt == null, ct)
                    .ConfigureAwait(false);

                int equippedBadges = await db
                    .PlayerBadges.AsNoTracking()
                    .CountAsync(b => b.DeletedAt == null && b.SlotId != null, ct)
                    .ConfigureAwait(false);

                int playersWithBadges = await db
                    .PlayerBadges.AsNoTracking()
                    .Where(b => b.DeletedAt == null)
                    .Select(b => b.PlayerEntityId)
                    .Distinct()
                    .CountAsync(ct)
                    .ConfigureAwait(false);

                var topBadgeRows = await db
                    .PlayerBadges.AsNoTracking()
                    .Where(b => b.DeletedAt == null)
                    .GroupBy(b => b.BadgeCode)
                    .Select(g => new
                    {
                        badgeCode = g.Key,
                        holders = g.Select(b => b.PlayerEntityId).Distinct().Count(),
                        equipped = g.Count(b => b.SlotId != null),
                    })
                    .OrderByDescending(g => g.holders)
                    .Take(limit)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                List<BadgeHolderCount> topBadges = topBadgeRows
                    .Select(b => new BadgeHolderCount(
                        b.badgeCode,
                        _assetUrls.BadgeImage(b.badgeCode),
                        b.holders,
                        b.equipped
                    ))
                    .ToList();

                int totalEffects = await db
                    .PlayerEffects.AsNoTracking()
                    .CountAsync(e => e.DeletedAt == null, ct)
                    .ConfigureAwait(false);

                int activatedEffects = await db
                    .PlayerEffects.AsNoTracking()
                    .CountAsync(e => e.DeletedAt == null && e.ActivatedAt != null, ct)
                    .ConfigureAwait(false);

                int selectedEffects = await db
                    .PlayerEffects.AsNoTracking()
                    .CountAsync(e => e.DeletedAt == null && e.IsSelected, ct)
                    .ConfigureAwait(false);

                var topEffectRows = await db
                    .PlayerEffects.AsNoTracking()
                    .Where(e => e.DeletedAt == null)
                    .GroupBy(e => e.EffectId)
                    .Select(g => new
                    {
                        effectId = g.Key,
                        owners = g.Select(e => e.PlayerEntityId).Distinct().Count(),
                        activated = g.Count(e => e.ActivatedAt != null),
                        selected = g.Count(e => e.IsSelected),
                    })
                    .OrderByDescending(g => g.owners)
                    .Take(limit)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                List<EffectOwnerCount> topEffects = topEffectRows
                    .Select(e => new EffectOwnerCount(
                        e.effectId,
                        _assetUrls.EffectImage(e.effectId),
                        e.owners,
                        e.activated,
                        e.selected
                    ))
                    .ToList();

                List<ChatStyleRow> chatStyles = await db
                    .PlayerChatStyles.AsNoTracking()
                    .OrderBy(s => s.ClientStyleId)
                    .Select(s => new ChatStyleRow(
                        s.Id,
                        s.ClientStyleId,
                        db.PlayerOwnedChatStyles.Count(o =>
                            o.ChatStyleId == s.Id && o.DeletedAt == null
                        )
                    ))
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                int wardrobeOutfits = await db
                    .PlayerWardrobeOutfits.AsNoTracking()
                    .CountAsync(o => o.DeletedAt == null, ct)
                    .ConfigureAwait(false);

                int wardrobeUsers = await db
                    .PlayerWardrobeOutfits.AsNoTracking()
                    .Where(o => o.DeletedAt == null)
                    .Select(o => o.PlayerEntityId)
                    .Distinct()
                    .CountAsync(ct)
                    .ConfigureAwait(false);

                var topCollectors = await db
                    .PlayerBadges.AsNoTracking()
                    .Where(b => b.DeletedAt == null)
                    .GroupBy(b => b.PlayerEntityId)
                    .Select(g => new { playerId = g.Key, badges = g.Count() })
                    .OrderByDescending(g => g.badges)
                    .Take(10)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                Dictionary<int, string> names = await db.PlayerNamesAsync(
                        DisplayNameQueries.NormalizeIds(
                            topCollectors.Select(c => (int?)c.playerId)
                        ),
                        ct
                    )
                    .ConfigureAwait(false);

                int distinctBadgeCodes = await db
                    .PlayerBadges.AsNoTracking()
                    .Where(b => b.DeletedAt == null)
                    .Select(b => b.BadgeCode)
                    .Distinct()
                    .CountAsync(ct)
                    .ConfigureAwait(false);

                return new PlayerRewardStats(
                    new PlayerRewardTotals(
                        totalBadges,
                        equippedBadges,
                        playersWithBadges,
                        distinctBadgeCodes,
                        totalEffects,
                        activatedEffects,
                        selectedEffects,
                        chatStyles.Count,
                        wardrobeOutfits,
                        wardrobeUsers
                    ),
                    // The forms below grant ids and codes that may not exist anywhere yet, so they
                    // build their own preview URL from these rather than looking one up.
                    _assetUrls.EffectImageTemplate,
                    _assetUrls.BadgeImageTemplate,
                    topBadges,
                    topEffects,
                    chatStyles,
                    topCollectors
                        .Select(c => new BadgeCollector(
                            c.playerId,
                            DisplayNameQueries.ResolvePlayerName(names, c.playerId),
                            c.badges
                        ))
                        .ToList()
                );
            },
            ct
        );

    /// <summary>Everything one player holds, for the investigation flow: their badges (equipped
    /// first), effects, chat styles and saved outfits in one call.</summary>
    public Task<PlayerRewardDetail?> PlayerRewardDetailAsync(int playerId, CancellationToken ct) =>
        QueryAsync<PlayerRewardDetail?>(
            async db =>
            {
                var player = await db
                    .Players.AsNoTracking()
                    .Where(p => p.Id == playerId)
                    .Select(p => new { p.Name, p.Figure })
                    .FirstOrDefaultAsync(ct)
                    .ConfigureAwait(false);

                if (player is null)
                {
                    return null;
                }

                string name = player.Name;

                var badgeRows = await db
                    .PlayerBadges.AsNoTracking()
                    .Where(b => b.PlayerEntityId == playerId && b.DeletedAt == null)
                    .OrderBy(b => b.SlotId == null)
                    .ThenBy(b => b.SlotId)
                    .Select(b => new
                    {
                        b.Id,
                        b.BadgeCode,
                        b.SlotId,
                        b.CreatedAt,
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                List<PlayerBadgeRow> badges = badgeRows
                    .Select(b => new PlayerBadgeRow(
                        b.Id,
                        b.BadgeCode,
                        _assetUrls.BadgeImage(b.BadgeCode),
                        b.SlotId,
                        b.CreatedAt
                    ))
                    .ToList();

                var effectRows = await db
                    .PlayerEffects.AsNoTracking()
                    .Where(e => e.PlayerEntityId == playerId && e.DeletedAt == null)
                    .OrderByDescending(e => e.IsSelected)
                    .ThenBy(e => e.EffectId)
                    .Select(e => new
                    {
                        e.Id,
                        e.EffectId,
                        e.SubType,
                        e.TotalDuration,
                        e.ActivatedAt,
                        e.IsSelected,
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                // Rendered on this player's own figure: an effect is only ever seen worn, and worn by
                // them is what the operator is checking.
                List<PlayerEffectRow> effects = effectRows
                    .Select(e => new PlayerEffectRow(
                        e.Id,
                        e.EffectId,
                        _assetUrls.EffectImage(e.EffectId, player.Figure),
                        e.SubType,
                        e.TotalDuration,
                        e.ActivatedAt,
                        e.IsSelected
                    ))
                    .ToList();

                List<PlayerChatStyleRow> chatStyles = await db
                    .PlayerOwnedChatStyles.AsNoTracking()
                    .Where(o => o.PlayerEntityId == playerId && o.DeletedAt == null)
                    // The catalogue's client id, not the foreign key: the number an operator can act
                    // on is the one the client uses, and the two are not the same.
                    .Select(o => new PlayerChatStyleRow(o.Id, o.ChatStyle.ClientStyleId))
                    .OrderBy(o => o.ClientStyleId)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                List<PlayerOutfitRow> outfits = await db
                    .PlayerWardrobeOutfits.AsNoTracking()
                    .Where(o => o.PlayerEntityId == playerId && o.DeletedAt == null)
                    .OrderBy(o => o.SlotId)
                    .Select(o => new PlayerOutfitRow(o.Id, o.SlotId, o.Figure, o.Gender))
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                return new PlayerRewardDetail(
                    playerId,
                    name,
                    _assetUrls.AvatarImage(player.Figure),
                    badges,
                    effects,
                    chatStyles,
                    outfits
                );
            },
            ct
        );
}
