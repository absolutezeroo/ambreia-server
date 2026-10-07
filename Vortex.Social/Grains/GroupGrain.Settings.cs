using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Entities.Groups;
using Vortex.Primitives.Events;
using Vortex.Primitives.Groups;
using Vortex.Primitives.Groups.Enums;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Server.Grains;

namespace Vortex.Social.Grains;

/// <summary>Guild identity, join policy, and the end of the guild's life.</summary>
internal sealed partial class GroupGrain
{
    public async Task<bool> UpdateIdentityAsync(
        PlayerId actor,
        string name,
        string description,
        CancellationToken ct
    )
    {
        // Renaming must clear the same bar as creating: without this an admin could rename a guild
        // to an empty string, or to something past the column's 50-char limit (which would throw at
        // SaveChanges rather than being refused cleanly).
        int maxNameLength = await this
            .GrainFactory.GetServerConfigGrain()
            .GetIntAsync(GroupConfig.MaxNameLengthKey, GroupConfig.MaxNameLengthDefault)
            .ConfigureAwait(true);

        if (GroupNameRules.Validate(name, maxNameLength) is string reason)
        {
            _logger.LogInformation(
                "Rejected rename of group {GroupId} by player {ActorId}: {Reason}",
                GroupId,
                actor.Value,
                reason
            );
            return false;
        }

        string trimmedName = name.Trim();

        return await MutateAsAdminAsync(
                actor,
                group =>
                {
                    group.Name = trimmedName;
                    group.Description = string.IsNullOrEmpty(description) ? null : description;
                },
                ct
            )
            .ConfigureAwait(true);
    }

    public Task<bool> UpdateColorsAsync(
        PlayerId actor,
        int primaryColorId,
        int secondaryColorId,
        CancellationToken ct
    )
    {
        return MutateAsAdminAsync(
            actor,
            group =>
            {
                group.ColorOne = primaryColorId.ToString();
                group.ColorTwo = secondaryColorId.ToString();
            },
            ct
        );
    }

    public Task<bool> UpdateBadgeAsync(
        PlayerId actor,
        IReadOnlyList<int> badgeParts,
        CancellationToken ct
    )
    {
        return MutateAsAdminAsync(
            actor,
            group => group.Badge = GroupDirectoryGrain.BuildBadgeCode(badgeParts),
            ct
        );
    }

    public async Task<bool> UpdateSettingsAsync(
        PlayerId actor,
        int guildType,
        int rightsLevel,
        CancellationToken ct
    )
    {
        // Settings (join policy + decoration rights) are owner-only.
        bool updated = await MutateAsync(
                actor,
                true,
                group =>
                {
                    if (guildType is >= 0 and <= 2)
                    {
                        group.Type = (GroupType)guildType;
                    }

                    group.AdminOnlyDecoration = rightsLevel != 0;
                },
                ct
            )
            .ConfigureAwait(true);

        if (updated)
        {
            // The decoration policy moves every plain member's build rights at once — an empty
            // affected list makes the room re-evaluate everyone standing in it.
            await NotifyBaseRoomAsync([], ct).ConfigureAwait(true);
        }

        return updated;
    }

    public async Task<bool> DeactivateAsync(PlayerId actor, CancellationToken ct)
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct);

        GroupEntity? group = await dbCtx.Groups.FirstOrDefaultAsync(
            g => g.Id == GroupId && g.DeletedAt == null,
            ct
        );
        if (group is null || group.OwnerPlayerEntityId != actor.Value)
        {
            return false;
        }

        return await DeactivateCoreAsync(dbCtx, group, actor.Value, ct).ConfigureAwait(true);
    }

    public async Task<bool> StaffDeactivateAsync(int actorPlayerId, CancellationToken ct)
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct);

        GroupEntity? group = await dbCtx.Groups.FirstOrDefaultAsync(
            g => g.Id == GroupId && g.DeletedAt == null,
            ct
        );

        // Only the ownership check differs. Everything the disband has to reach -- the room link,
        // the member rows, the requests, the cached roster that carries build rights -- is below,
        // and an operator's version that reimplemented any of it would be the one that forgot the
        // roster and left ex-members building in a guild that no longer exists.
        return group is not null
            && await DeactivateCoreAsync(dbCtx, group, actorPlayerId, ct).ConfigureAwait(true);
    }

    private async Task<bool> DeactivateCoreAsync(
        VortexDbContext dbCtx,
        GroupEntity group,
        int actorId,
        CancellationToken ct
    )
    {
        DateTime now = DateTime.UtcNow;
        int baseRoomId = group.RoomEntityId;

        // Detach the room (clears the circular link), then soft-delete the group graph.
        await dbCtx
            .Rooms.Where(r => r.GroupEntityId == GroupId)
            .ExecuteUpdateAsync(up => up.SetProperty(r => r.GroupEntityId, (int?)null), ct)
            .ConfigureAwait(true);

        await dbCtx
            .GroupMembers.Where(m => m.GroupEntityId == GroupId && m.DeletedAt == null)
            .ExecuteUpdateAsync(up => up.SetProperty(m => m.DeletedAt, now), ct)
            .ConfigureAwait(true);

        await dbCtx
            .GroupMembershipRequests.Where(r => r.GroupEntityId == GroupId && r.DeletedAt == null)
            .ExecuteUpdateAsync(up => up.SetProperty(r => r.DeletedAt, now), ct)
            .ConfigureAwait(true);

        group.DeletedAt = now;
        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

        await _events
            .PublishAsync(new GroupDeactivatedEvent(actorId, GroupId), ct)
            .ConfigureAwait(true);

        // The room is no longer a guild base — drop the cached roster so ex-members immediately
        // lose the build rights the guild was granting them.
        await NotifyRoomAsync(baseRoomId, [], ct).ConfigureAwait(true);

        _logger.LogInformation("Group {GroupId} deactivated by player {ActorId}", GroupId, actorId);
        return true;
    }

    private Task<bool> MutateAsAdminAsync(
        PlayerId actor,
        Action<GroupEntity> mutate,
        CancellationToken ct
    )
    {
        return MutateAsync(actor, false, mutate, ct);
    }

    private async Task<bool> MutateAsync(
        PlayerId actor,
        bool ownerOnly,
        Action<GroupEntity> mutate,
        CancellationToken ct
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct);

        GroupEntity? group = await dbCtx.Groups.FirstOrDefaultAsync(
            g => g.Id == GroupId && g.DeletedAt == null,
            ct
        );
        if (group is null)
        {
            return false;
        }

        int actorId = actor.Value;

        if (ownerOnly)
        {
            if (group.OwnerPlayerEntityId != actorId)
            {
                return false;
            }
        }
        else if (!await IsAdminAsync(dbCtx, group, actorId, ct).ConfigureAwait(true))
        {
            return false;
        }

        mutate(group);
        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

        await _events
            .PublishAsync(new GroupUpdatedEvent(actorId, GroupId), ct)
            .ConfigureAwait(true);

        return true;
    }
}
