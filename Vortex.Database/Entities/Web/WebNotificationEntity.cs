using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Notifications;

namespace Vortex.Database.Entities.Web;

/// <summary>One thing a player is told, on the website.</summary>
/// <remarks>
/// <para>
/// <c>web_notifications</c>, beside the articles and the feed, because the website is where these
/// are read and the game client will not read them. What is written here is written by handlers
/// subscribed to the hotel's own events — so the PRODUCERS are all over the emulator and the one
/// consumer is the site, and it is the consumer the name follows.
/// </para>
/// <para>
/// <see cref="Data"/> is denormalised on purpose. A notification has to read correctly long after
/// the thing it is about is gone: "your offer for <i>Throne</i> sold" must still name the throne
/// once the offer row has been cleaned up, and a join would give an empty line instead.
/// </para>
/// </remarks>
[Table("web_notifications")]
// The only query there is: my notifications, newest first. Descending on the date because a page
// of notifications is always the most recent ones.
[Index(nameof(PlayerEntityId), nameof(CreatedAt), AllDescending = true)]
public class WebNotificationEntity : VortexEntity
{
    /// <summary>The few values the website needs to word the line, as JSON. See the remarks.</summary>
    public const int DataLength = 512;

    [Column("player_id")]
    public required int PlayerEntityId { get; set; }

    [Column("kind")]
    public required NotificationKind Kind { get; set; }

    /// <summary>Who caused it, when that is a player rather than the hotel itself.</summary>
    [Column("actor_player_id")]
    public int? ActorPlayerEntityId { get; set; }

    /// <summary>
    /// What it points at, read together with <see cref="Kind"/> — a post, a room, a group. No
    /// foreign key: it names a different table per kind, and the row has to survive whatever it
    /// pointed at being deleted.
    /// </summary>
    [Column("target_id")]
    public int? TargetId { get; set; }

    [Column("data")]
    [MaxLength(DataLength)]
    public string? Data { get; set; }

    /// <summary>
    /// When the player last had this in front of them. Null is unread, and the unread count is
    /// what the bell shows.
    /// </summary>
    [Column("seen_at")]
    public DateTime? SeenAt { get; set; }

    [ForeignKey(nameof(PlayerEntityId))]
    public PlayerEntity? PlayerEntity { get; set; }

    [ForeignKey(nameof(ActorPlayerEntityId))]
    public PlayerEntity? ActorPlayerEntity { get; set; }
}
