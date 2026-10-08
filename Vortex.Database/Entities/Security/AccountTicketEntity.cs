using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Entities.Players;

namespace Vortex.Database.Entities.Security;

/// <summary>
/// A sign-in ticket for an account rather than a player (spec F3a §5.1): it opens the game socket
/// on the character screen. Same lifetime rules as <see cref="SecurityTicketEntity"/>.
/// </summary>
[Table("account_tickets")]
[Index(nameof(AccountId), IsUnique = true)]
[Index(nameof(Ticket), IsUnique = true)]
public class AccountTicketEntity : VortexEntity
{
    [Column("account_id")]
    public required int AccountId { get; set; }

    [Column("ticket")]
    [MaxLength(128)]
    public required string Ticket { get; set; }

    [Column("ip_address")]
    [MaxLength(64)]
    public required string IpAddress { get; set; }

    /// <summary>Null until the first use: the ticket then expires the configured TTL after
    /// <c>CreatedAt</c>, like a player ticket.</summary>
    [Column("expires_at")]
    public DateTime? ExpiresAt { get; set; }

    [ForeignKey(nameof(AccountId))]
    public PlayerAccountEntity? Account { get; set; }
}
