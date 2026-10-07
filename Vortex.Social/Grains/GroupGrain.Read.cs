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
using Vortex.Primitives.Groups;
using Vortex.Primitives.Groups.Enums;
using Vortex.Primitives.Groups.Snapshots;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Server.Grains;
using Vortex.Social.Groups;

namespace Vortex.Social.Grains;

/// <summary>Read paths: everything the guild windows poll while they are open.</summary>
internal sealed partial class GroupGrain
{
    /// <summary>The members window asks for pending join requests with this search type.</summary>
    private const int PendingRequestsSearchType = 1;

    public async Task<GroupDetailsSnapshot?> GetDetailsAsync(PlayerId viewer, CancellationToken ct)
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct);

        GroupEntity? group = await dbCtx
            .Groups.AsNoTracking()
            .Include(g => g.RoomEntity)
            .Include(g => g.OwnerPlayerEntity)
            .Include(g => g.ForumSettings)
            .FirstOrDefaultAsync(g => g.Id == GroupId && g.DeletedAt == null, ct);

        if (group is null)
        {
            return null;
        }

        int viewerId = viewer.Value;

        GroupMemberEntity? membership = await dbCtx
            .GroupMembers.AsNoTracking()
            .FirstOrDefaultAsync(
                m =>
                    m.GroupEntityId == GroupId
                    && m.PlayerEntityId == viewerId
                    && m.DeletedAt == null,
                ct
            );

        bool hasPendingRequest =
            membership is null
            && await dbCtx
                .GroupMembershipRequests.AsNoTracking()
                .AnyAsync(
                    r =>
                        r.GroupEntityId == GroupId
                        && r.PlayerEntityId == viewerId
                        && r.DeletedAt == null,
                    ct
                );

        int totalMembers = await dbCtx.GroupMembers.CountAsync(
            m => m.GroupEntityId == GroupId && m.DeletedAt == null,
            ct
        );

        int pendingCount = await dbCtx.GroupMembershipRequests.CountAsync(
            r => r.GroupEntityId == GroupId && r.DeletedAt == null,
            ct
        );

        bool favourite = await dbCtx
            .Players.AsNoTracking()
            .Where(p => p.Id == viewerId)
            .Select(p => p.FavouriteGroupId == GroupId)
            .FirstOrDefaultAsync(ct);

        bool isOwner = group.OwnerPlayerEntityId == viewerId;

        // Not IsAdminAsync: the membership row is already in hand, so re-querying for the rank would
        // buy nothing but a second round-trip.
        bool isAdmin = isOwner || membership?.Rank == GroupMemberRank.Admin;

        GroupMembershipStatus status =
            membership is not null ? GroupMembershipStatus.Member
            : hasPendingRequest ? GroupMembershipStatus.RequestPending
            : GroupMembershipStatus.NotMember;

        return new GroupDetailsSnapshot
        {
            GroupId = group.Id,
            IsGuild = true,
            Type = (int)group.Type,
            Name = group.Name,
            Description = group.Description ?? string.Empty,
            BadgeCode = group.Badge,
            RoomId = group.RoomEntityId,
            RoomName = group.RoomEntity.Name,
            Status = status,
            TotalMembers = totalMembers,
            Favourite = favourite,
            CreationDate = group.CreatedAt.ToString("dd-MM-yyyy HH:mm"),
            IsOwner = isOwner,
            IsAdmin = isAdmin,
            OwnerName = group.OwnerPlayerEntity.Name,
            OpenDetails = false,
            MembersCanDecorate = !group.AdminOnlyDecoration,
            PendingMemberCount = pendingCount,
            HasForum = group.ForumSettings?.Enabled ?? false,
        };
    }

    public async Task<GroupMemberRank?> GetMemberRankAsync(PlayerId player, CancellationToken ct)
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct);

        int playerId = player.Value;

        return await dbCtx
            .GroupMembers.AsNoTracking()
            .Where(m =>
                m.GroupEntityId == GroupId && m.PlayerEntityId == playerId && m.DeletedAt == null
            )
            .Select(m => (GroupMemberRank?)m.Rank)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<GroupMembersPageSnapshot?> GetMembersAsync(
        PlayerId viewer,
        int pageIndex,
        string userNameFilter,
        int searchType,
        CancellationToken ct
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct);

        GroupEntity? group = await dbCtx
            .Groups.AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == GroupId && g.DeletedAt == null, ct);

        if (group is null)
        {
            return null;
        }

        int viewerId = viewer.Value;
        bool allowedToManage = await IsAdminAsync(dbCtx, group, viewerId, ct).ConfigureAwait(true);

        // searchType 1 = pending join requests; otherwise current members.
        bool wantsPendingRequests = searchType == PendingRequestsSearchType;

        // The real client only offers this tab to admins, so it never asks — but searchType arrives
        // straight off the wire, and the answer names every player waiting to join. Gate the query
        // itself; shipping allowedToManage in the reply only disarmed the client's own UI.
        if (wantsPendingRequests && !allowedToManage)
        {
            _logger.LogInformation(
                "Player {ViewerId} asked for the pending join requests of group {GroupId} without "
                    + "administering it",
                viewerId,
                GroupId
            );

            return null;
        }

        int membersPerPage = await this
            .GrainFactory.GetServerConfigGrain()
            .GetIntAsync(GroupConfig.MembersPerPageKey, GroupConfig.MembersPerPageDefault)
            .ConfigureAwait(true);

        string filter = userNameFilter?.Trim() ?? string.Empty;
        int skip = Math.Max(pageIndex, 0) * membersPerPage;

        List<GroupMemberSnapshot> members;
        int totalEntries;

        if (wantsPendingRequests)
        {
            IQueryable<GroupMembershipRequestEntity> query = dbCtx
                .GroupMembershipRequests.AsNoTracking()
                .Include(r => r.PlayerEntity)
                .Where(r => r.GroupEntityId == GroupId && r.DeletedAt == null);

            if (filter.Length > 0)
            {
                query = query.Where(r => r.PlayerEntity.Name.Contains(filter));
            }

            totalEntries = await query.CountAsync(ct);

            members =
            [
                .. (
                    await query.OrderBy(r => r.Id).Skip(skip).Take(membersPerPage).ToListAsync(ct)
                ).Select(GroupMemberMapper.FromRequest),
            ];
        }
        else
        {
            IQueryable<GroupMemberEntity> query = dbCtx
                .GroupMembers.AsNoTracking()
                .Include(m => m.PlayerEntity)
                .Where(m => m.GroupEntityId == GroupId && m.DeletedAt == null);

            if (filter.Length > 0)
            {
                query = query.Where(m => m.PlayerEntity.Name.Contains(filter));
            }

            totalEntries = await query.CountAsync(ct);

            members =
            [
                .. (
                    await query
                        // Admins first, then members; stable by id within a rank.
                        .OrderByDescending(m => m.Rank)
                        .ThenBy(m => m.Id)
                        .Skip(skip)
                        .Take(membersPerPage)
                        .ToListAsync(ct)
                ).Select(m => GroupMemberMapper.FromMember(m, group.OwnerPlayerEntityId)),
            ];
        }

        return new GroupMembersPageSnapshot
        {
            GroupId = group.Id,
            GroupName = group.Name,
            BaseRoomId = group.RoomEntityId,
            BadgeCode = group.Badge,
            TotalEntries = totalEntries,
            Members = members,
            AllowedToManage = allowedToManage,
            PageSize = membersPerPage,
            PageIndex = pageIndex,
            SearchType = searchType,
            UserNameFilter = filter,
        };
    }

    public async Task<GroupEditInfoSnapshot?> GetEditInfoAsync(PlayerId actor, CancellationToken ct)
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct);

        GroupEntity? group = await dbCtx
            .Groups.AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == GroupId && g.DeletedAt == null, ct);
        if (group is null)
        {
            return null;
        }

        int actorId = actor.Value;
        bool isOwner = group.OwnerPlayerEntityId == actorId;

        if (!await IsAdminAsync(dbCtx, group, actorId, ct).ConfigureAwait(true))
        {
            return null;
        }

        List<GroupRoomSnapshot> ownedRooms = await dbCtx
            .Rooms.AsNoTracking()
            .Where(r => r.PlayerEntityId == group.OwnerPlayerEntityId && r.DeletedAt == null)
            .OrderBy(r => r.Id)
            .Select(r => new GroupRoomSnapshot
            {
                RoomId = r.Id,
                RoomName = r.Name,
                HasControllers = r.GroupEntityId == null || r.Id == group.RoomEntityId,
            })
            .ToListAsync(ct);

        int membershipCount = await dbCtx.GroupMembers.CountAsync(
            m => m.GroupEntityId == GroupId && m.DeletedAt == null,
            ct
        );

        return new GroupEditInfoSnapshot
        {
            OwnedRooms = ownedRooms,
            IsOwner = isOwner,
            GroupId = group.Id,
            GroupName = group.Name,
            GroupDescription = group.Description ?? string.Empty,
            BaseRoomId = group.RoomEntityId,
            PrimaryColorId = ParseColorId(group.ColorOne),
            SecondaryColorId = ParseColorId(group.ColorTwo),
            GuildType = (int)group.Type,
            GuildRightsLevel = group.AdminOnlyDecoration ? 1 : 0,
            Locked = group.Type == GroupType.Private,
            Url = string.Empty,
            // Parse the stored badge code into exactly 5 layer entries (one per client layer).
            // The client iterates all 5 unconditionally — a short list causes a null-reference crash.
            BadgeParts = GuildBadgeLibrary.ParseBadgeCode(group.Badge),
            BadgeCode = group.Badge,
            MembershipCount = membershipCount,
        };
    }

    public async Task<int> GetMemberFurniCountAsync(int targetPlayerId, CancellationToken ct)
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct);

        int? baseRoomId = await dbCtx
            .Groups.AsNoTracking()
            .Where(g => g.Id == GroupId && g.DeletedAt == null)
            .Select(g => (int?)g.RoomEntityId)
            .FirstOrDefaultAsync(ct);

        if (baseRoomId is null)
        {
            return 0;
        }

        return await dbCtx.Furnitures.CountAsync(
            f =>
                f.PlayerEntityId == targetPlayerId
                && f.RoomEntityId == baseRoomId
                && f.DeletedAt == null,
            ct
        );
    }

    private static int ParseColorId(string value)
    {
        return int.TryParse(value, out int id) ? id : 0;
    }
}
