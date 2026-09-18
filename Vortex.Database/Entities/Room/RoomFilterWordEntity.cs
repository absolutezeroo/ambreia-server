using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Vortex.Database.Entities.Room;

/// <summary>
/// One word a room owner has banned from their own room's chat. The room settings dialog's filter
/// tab reads and writes this list, and <c>RoomChatSystem</c> is what makes it mean anything.
/// </summary>
[Table("room_filter_words")]
[Index(nameof(RoomEntityId), nameof(Word), IsUnique = true)]
public class RoomFilterWordEntity : VortexEntity
{
    [Column("room_id")]
    public required int RoomEntityId { get; set; }

    /// <summary>
    /// Stored as the owner typed it; matching is case-insensitive, which is why the unique index
    /// holds — MySQL's default collation compares it that way, so "Bobba" cannot be added twice.
    /// </summary>
    [Column("word")]
    [MaxLength(64)]
    public required string Word { get; set; }

    [ForeignKey(nameof(RoomEntityId))]
    public required RoomEntity RoomEntity { get; set; }
}
