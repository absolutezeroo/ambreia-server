using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Badges.Snapshots;
using Vortex.Primitives.Events;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Players.Snapshots;

namespace Vortex.Progression.Grains;

internal sealed class PlayerBadgeGrain(
    IDbContextFactory<VortexDbContext> dbCtxFactory,
    IGrainFactory grainFactory,
    IEventPublisher events,
    ILogger<PlayerBadgeGrain> logger
) : Grain, IPlayerBadgeGrain
{
    /// <summary>The badge editor has five display slots and the client sends exactly five pairs.</summary>
    private const int MaxSlot = 5;

    private readonly IDbContextFactory<VortexDbContext> _dbCtxFactory = dbCtxFactory;
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly IEventPublisher _events = events;
    private readonly ILogger<PlayerBadgeGrain> _logger = logger;

    private int PlayerId => (int)this.GetPrimaryKeyLong();

    public async Task<ImmutableArray<PlayerBadgeSnapshot>> GetBadgesAsync(CancellationToken ct)
    {
        try
        {
            await using VortexDbContext dbCtx = await _dbCtxFactory
                .CreateDbContextAsync(ct)
                .ConfigureAwait(true);

            List<PlayerBadgeEntity> entities = await dbCtx
                .PlayerBadges.AsNoTracking()
                .Where(b => b.PlayerEntityId == PlayerId)
                .ToListAsync(ct)
                .ConfigureAwait(true);

            // The owner count and the rarity tier are hotel figures, not this player's, so they
            // come from the badge directory rather than from `player_badges`. Sending them as 0
            // was not "no opinion": the client draws whatever arrives in those two fields, so it
            // was the claim that every badge in the hotel is common.
            return await DescribeAsync(
                    entities.Select(b => (b.SlotId ?? 0, b.BadgeCode)).ToList(),
                    ct
                )
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load badges for player {PlayerId}", PlayerId);
            return ImmutableArray<PlayerBadgeSnapshot>.Empty;
        }
    }

    /// <summary>
    ///     The five slots the client sends, reduced to the ones worth writing. A slot outside 1..5
    ///     has nowhere to render, an empty code is an empty slot, and a code or a slot claimed
    ///     twice would make <c>ToDictionary</c> throw — after the update that clears every slot had
    ///     already committed, so a crafted packet wiped the selection instead of changing it.
    /// </summary>
    public async Task<ImmutableArray<BadgeRarityCountSnapshot>> GetRarityCountsAsync(
        CancellationToken ct
    )
    {
        try
        {
            await using VortexDbContext dbCtx = await _dbCtxFactory
                .CreateDbContextAsync(ct)
                .ConfigureAwait(true);

            List<string> codes = await dbCtx
                .PlayerBadges.AsNoTracking()
                .Where(b => b.PlayerEntityId == PlayerId)
                .Select(b => b.BadgeCode)
                .ToListAsync(ct)
                .ConfigureAwait(true);

            if (codes.Count == 0)
            {
                return [];
            }

            return await _grainFactory
                .GetBadgeDirectoryGrain()
                .CountByRarityAsync(codes, ct)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to count badge rarities for player {PlayerId}", PlayerId);
            return [];
        }
    }

    /// <summary>
    /// Attaches the hotel's owner count and rarity tier to each badge. One call to the directory
    /// for the whole list, not one per badge: the wire carries the four fields together and a
    /// player with forty badges would otherwise make forty grain calls to fill one packet.
    /// </summary>
    private async Task<ImmutableArray<PlayerBadgeSnapshot>> DescribeAsync(
        List<(int SlotId, string BadgeCode)> badges,
        CancellationToken ct
    )
    {
        if (badges.Count == 0)
        {
            return [];
        }

        ImmutableArray<BadgeInfoSnapshot> info = await _grainFactory
            .GetBadgeDirectoryGrain()
            .GetInfoAsync([.. badges.Select(b => b.BadgeCode)], ct)
            .ConfigureAwait(true);

        return
        [
            .. badges.Select(
                (badge, index) =>
                    new PlayerBadgeSnapshot
                    {
                        SlotId = badge.SlotId,
                        BadgeCode = badge.BadgeCode,
                        OwnerCount = info[index].OwnerCount,
                        BadgeRarityId = (int)info[index].Rarity,
                    }
            ),
        ];
    }

    internal static Dictionary<string, int> NormalizeSlots(
        List<(int SlotId, string BadgeCode)> slots
    ) =>
        slots
            .Where(s => s.SlotId is > 0 and <= MaxSlot && !string.IsNullOrWhiteSpace(s.BadgeCode))
            .DistinctBy(s => s.BadgeCode)
            .DistinctBy(s => s.SlotId)
            .ToDictionary(s => s.BadgeCode, s => s.SlotId);

    public async Task<ImmutableArray<PlayerBadgeSnapshot>> SetActivatedBadgesAsync(
        List<(int SlotId, string BadgeCode)> slots,
        CancellationToken ct
    )
    {
        ImmutableArray<PlayerBadgeSnapshot> equipped = [];

        try
        {
            await using VortexDbContext dbCtx = await _dbCtxFactory
                .CreateDbContextAsync(ct)
                .ConfigureAwait(true);

            await dbCtx
                .PlayerBadges.Where(b => b.PlayerEntityId == PlayerId && b.SlotId > 0)
                .ExecuteUpdateAsync(up => up.SetProperty(b => b.SlotId, 0), ct)
                .ConfigureAwait(true);

            Dictionary<string, int> desiredSlotByCode = NormalizeSlots(slots);

            if (desiredSlotByCode.Count > 0)
            {
                List<string> codes = [.. desiredSlotByCode.Keys];

                List<PlayerBadgeEntity> toActivate = await dbCtx
                    .PlayerBadges.Where(b =>
                        b.PlayerEntityId == PlayerId && codes.Contains(b.BadgeCode)
                    )
                    .ToListAsync(ct)
                    .ConfigureAwait(true);

                foreach (PlayerBadgeEntity entity in toActivate)
                {
                    entity.SlotId = desiredSlotByCode[entity.BadgeCode];
                }

                await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

                // The rows that were found, not the codes that were asked for: a request naming a
                // badge the player does not own equips nothing, and reporting it worn would put a
                // badge on the avatar that no row backs.
                equipped = await DescribeAsync(
                        toActivate
                            .OrderBy(b => b.SlotId)
                            .Select(b => (b.SlotId ?? 0, b.BadgeCode))
                            .ToList(),
                        ct
                    )
                    .ConfigureAwait(true);
            }

            // Published for an empty selection too: taking every badge off is a change worth a line,
            // and a timeline that only ever records putting them on cannot show the difference.
            await _events
                .PublishAsync(
                    new BadgesEquippedEvent(PlayerId, [.. equipped.Select(b => b.BadgeCode)]),
                    ct
                )
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update badge slots for player {PlayerId}", PlayerId);
        }

        return equipped;
    }
}
