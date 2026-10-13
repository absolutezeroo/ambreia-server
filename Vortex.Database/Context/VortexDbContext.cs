using Microsoft.EntityFrameworkCore;
using Vortex.Database.Entities.Audit;
using Vortex.Database.Entities.Errors;
using Vortex.Database.Entities.Furniture;
using Vortex.Database.Entities.Permissions;
using Vortex.Database.Entities.Players;
using Vortex.Database.Entities.Room;
using Vortex.Database.Entities.Rpg;
using Vortex.Database.Entities.Security;
using Vortex.Database.Entities.Server;
using Vortex.Database.Entities.World;

namespace Vortex.Database.Context;

public class VortexDbContext(DbContextOptions<VortexDbContext> options)
    : DbContextBase<VortexDbContext>(options)
{
    public DbSet<AuditEventEntity> AuditEvents { get; init; } = null!;

    public DbSet<ItemEventEntity> ItemEvents { get; init; } = null!;

    public DbSet<FurnitureDefinitionEntity> FurnitureDefinitions { get; init; } = null!;

    public DbSet<FurnitureEntity> Furnitures { get; init; } = null!;

    public DbSet<PlayerEntity> Players { get; init; } = null!;

    public DbSet<RoomEntity> Rooms { get; init; } = null!;

    public DbSet<RoomModelEntity> RoomModels { get; init; } = null!;

    public DbSet<RoomRightEntity> RoomRights { get; init; } = null!;

    public DbSet<RoomEntryLogEntity> RoomEntryLogs { get; init; } = null!;

    public DbSet<SecurityTicketEntity> SecurityTickets { get; init; } = null!;

    public DbSet<AccountTicketEntity> AccountTickets { get; init; } = null!;

    public DbSet<PlayerAccountEntity> PlayerAccounts { get; init; } = null!;

    public DbSet<RoleEntity> Roles { get; init; } = null!;

    public DbSet<RolePermissionEntity> RolePermissions { get; init; } = null!;

    public DbSet<PlayerAccountRoleEntity> PlayerAccountRoles { get; init; } = null!;

    public DbSet<ErrorGroupEntity> ErrorGroups { get; init; } = null!;

    public DbSet<ErrorOccurrenceEntity> ErrorOccurrences { get; init; } = null!;

    public DbSet<ServerConfigEntity> ServerConfig { get; init; } = null!;

    public DbSet<ZoneEntity> Zones { get; init; } = null!;

    public DbSet<ZoneVersionEntity> ZoneVersions { get; init; } = null!;

    public DbSet<RpgContentEntity> RpgContent { get; init; } = null!;

    public DbSet<RpgContentPackEntity> RpgContentPacks { get; init; } = null!;

    public DbSet<RpgContentMigrationEntity> RpgContentMigrations { get; init; } = null!;

    public DbSet<RpgContentTextEntity> RpgContentTexts { get; init; } = null!;

    public DbSet<PlayerCharacterEntity> PlayerCharacters { get; init; } = null!;

    public DbSet<RpgItemEntity> RpgItems { get; init; } = null!;

    public DbSet<RpgItemGrantEntity> RpgItemGrants { get; init; } = null!;

    public DbSet<RpgXpGrantEntity> RpgXpGrants { get; init; } = null!;

    public DbSet<RpgCharacterQuestEntity> RpgCharacterQuests { get; init; } = null!;
}
