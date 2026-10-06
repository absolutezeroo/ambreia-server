using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Entities.Players;

namespace Vortex.Database.Entities.Catalog;

[Table("catalog_ltd_raffle_entries")]
[Index(nameof(SeriesEntityId))]
[Index(nameof(PlayerEntityId))]
// The rarity, made real by the database. A serial number is what an LTD *is*, and it was being
// assigned by reading the highest one and adding one -- two winners drawn in the same moment read
// the same total and both write the same number, and the hotel has two number sevens of a series
// of a hundred forever. The unique index below is what makes the second write fail instead.
//
// SerialNumber is nullable and MySQL permits many NULLs under a unique index, so entries that have
// not won anything yet do not collide with each other.
[Index(nameof(SeriesEntityId), nameof(SerialNumber), IsUnique = true)]
public class LtdRaffleEntryEntity : VortexEntity
{
    [Column("series_id")]
    public required int SeriesEntityId { get; set; }

    [Column("player_id")]
    public required int PlayerEntityId { get; set; }

    [Column("batch_id")]
    [MaxLength(36)]
    public required string BatchId { get; set; }

    [Column("entered_at")]
    public required DateTime EnteredAt { get; set; }

    [Column("result")]
    [MaxLength(20)]
    [DefaultValue("pending")]
    public required string Result { get; set; }

    [Column("serial_number")]
    public int? SerialNumber { get; set; }

    [Column("processed_at")]
    public DateTime? ProcessedAt { get; set; }

    [ForeignKey(nameof(SeriesEntityId))]
    public required LtdSeriesEntity SeriesEntity { get; set; }

    [ForeignKey(nameof(PlayerEntityId))]
    public required PlayerEntity PlayerEntity { get; set; }
}
