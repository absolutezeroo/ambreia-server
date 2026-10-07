using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Entities.Players;

namespace Vortex.Database.Entities.Security;

[Table("security_tickets")]
[Index(nameof(PlayerEntityId), IsUnique = true)]
[Index(nameof(Ticket), IsUnique = true)]
public class SecurityTicketEntity : VortexEntity
{
    [Column("player_id")]
    public int PlayerEntityId { get; set; }

    [Column("ticket")]
    public required string Ticket { get; set; }

    [Column("ip_address")]
    public required string IpAddress { get; set; }

    [Column("is_locked")]
    [DefaultValue(false)]
    public bool IsLocked { get; set; }

    /// <summary>
    /// UTC timestamp after which this ticket is no longer valid, slid forward on each use of an
    /// unlocked ticket. Null until the first use: the ticket then expires the configured TTL after
    /// <c>CreatedAt</c> (never, when the TTL is 0).
    /// </summary>
    [Column("expires_at")]
    public DateTime? ExpiresAt { get; set; }

    [ForeignKey(nameof(PlayerEntityId))]
    public required PlayerEntity PlayerEntity { get; set; }
}
