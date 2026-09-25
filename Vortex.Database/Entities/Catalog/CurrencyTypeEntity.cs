using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Players.Enums.Wallet;

namespace Vortex.Database.Entities.Catalog;

[Table("currency_types")]
public class CurrencyTypeEntity : VortexEntity
{
    [Column("name")]
    public required string? Name { get; set; }

    [Column("type")]
    public required CurrencyType CurrencyType { get; set; }

    [Column("activity_point_type")]
    public int? ActivityPointType { get; set; }

    [Column("enabled")]
    [DefaultValue(true)]
    public required bool Enabled { get; set; }

    /// <summary>Balance granted to a player's first-ever <see cref="PlayerCurrencyEntity"/> row for
    /// this currency (admin-editable here instead of hardcoded in the grain). Defaults to the value
    /// previously hardcoded in <c>PlayerWalletGrain</c>, so existing rows keep the same behavior
    /// until an operator changes them.</summary>
    [Column("starting_amount")]
    [DefaultValue(200)]
    public int StartingAmount { get; set; } = 200;

    public List<PlayerCurrencyEntity>? PlayerCurrencies { get; set; }
}
