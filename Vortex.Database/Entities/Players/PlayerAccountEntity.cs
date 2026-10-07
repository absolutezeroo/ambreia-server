using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Vortex.Database.Entities.Players;

[Table("player_accounts")]
[Index(nameof(Email), IsUnique = true)]
public class PlayerAccountEntity : VortexEntity
{
    [Column("email")]
    public required string Email { get; set; }

    [Column("password_hash")]
    public required string PasswordHash { get; set; }

    [InverseProperty("PlayerAccount")]
    public List<PlayerEntity>? Players { get; set; }
}
