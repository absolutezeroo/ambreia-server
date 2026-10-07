using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Vortex.Primitives.Navigator.Enums;

namespace Vortex.Database.Entities.Players;

/// <summary>
/// Per-player account preferences echoed back to the client in the account-preferences packet on
/// login (volumes, free-flow chat / room-camera / room-invite toggles, UI flags). Everything the
/// settings UI persists lives here so it survives a reconnect.
/// </summary>
[Table("player_account_preferences")]
[Index(nameof(PlayerEntityId), IsUnique = true)]
public class PlayerAccountPreferencesEntity : VortexEntity
{
    [Column("player_id")]
    public required int PlayerEntityId { get; set; }

    [Column("ui_volume")]
    [DefaultValue(100)]
    public required int UiVolume { get; set; }

    [Column("furni_volume")]
    [DefaultValue(100)]
    public required int FurniVolume { get; set; }

    [Column("trax_volume")]
    [DefaultValue(100)]
    public required int TraxVolume { get; set; }

    [Column("free_flow_chat_disabled")]
    [DefaultValue(false)]
    public required bool FreeFlowChatDisabled { get; set; }

    [Column("room_invites_ignored")]
    [DefaultValue(false)]
    public required bool RoomInvitesIgnored { get; set; }

    [Column("room_camera_follow_disabled")]
    [DefaultValue(false)]
    public required bool RoomCameraFollowDisabled { get; set; }

    // Default 3 = UIFlags.FriendBarExpanded (1) | UIFlags.RoomToolsExpanded (2), the client's default
    // expanded panels.
    [Column("ui_flags")]
    [DefaultValue(3)]
    public required int UiFlags { get; set; }

    // The chat settings the client's own dialog writes and expects back in the account-preferences
    // packet — they are per account, not per room, since this revision (the room contributes only
    // its flood sensitivity). The defaults are the client's own for an absent field
    // (unknowns/_SafePkg_1927/_SafeCls_1926.as:179-210): font size 0, free-flow mode, normal
    // bubble, normal scroll speed.
    [Column("chat_size_preference")]
    [DefaultValue(0)]
    public required int ChatSizePreference { get; set; }

    [Column("chat_mode")]
    [DefaultValue(ChatModeType.FreeFlow)]
    public required ChatModeType ChatMode { get; set; }

    [Column("chat_bubble_width")]
    [DefaultValue(ChatBubbleWidthType.Normal)]
    public required ChatBubbleWidthType ChatBubbleWidth { get; set; }

    [Column("chat_scroll_speed")]
    [DefaultValue(ChatScrollSpeedType.Normal)]
    public required ChatScrollSpeedType ChatScrollSpeed { get; set; }

    // Discord Rich Presence: the four toggles of the client's own settings dialog, plus the version
    // of the consent dialog the player answered. Version 0 means "never answered" and is what makes
    // the client show the opt-in popup once — it is the client's mechanism, not ours, so it is
    // stored verbatim and never defaulted to the current version.
    [Column("discord_settings_version")]
    [DefaultValue(0)]
    public required int DiscordSettingsVersion { get; set; }

    [Column("discord_show_habbo")]
    [DefaultValue(true)]
    public required bool DiscordShowHabbo { get; set; }

    [Column("discord_share_activity")]
    [DefaultValue(true)]
    public required bool DiscordShareActivity { get; set; }

    [Column("discord_hide_in_hidden_rooms")]
    [DefaultValue(true)]
    public required bool DiscordHideInHiddenRooms { get; set; }

    [Column("discord_allow_joining")]
    [DefaultValue(true)]
    public required bool DiscordAllowJoining { get; set; }

    [ForeignKey(nameof(PlayerEntityId))]
    public PlayerEntity? PlayerEntity { get; set; }
}
