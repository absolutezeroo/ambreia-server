using Orleans;
using Vortex.Primitives.Navigator.Enums;

namespace Vortex.Primitives.Orleans.Snapshots.Players;

/// <summary>
/// The player's persisted account preferences (everything the settings UI stores except the chat
/// bubble style, which lives on the player row). Surfaced to the client in the account-preferences
/// packet on login so the settings dialog reflects the saved selection.
/// </summary>
[GenerateSerializer, Immutable]
public sealed record PlayerAccountPreferencesSnapshot
{
    [Id(0)]
    public required int UiVolume { get; init; }

    [Id(1)]
    public required int FurniVolume { get; init; }

    [Id(2)]
    public required int TraxVolume { get; init; }

    [Id(3)]
    public required bool FreeFlowChatDisabled { get; init; }

    [Id(4)]
    public required bool RoomInvitesIgnored { get; init; }

    [Id(5)]
    public required bool RoomCameraFollowDisabled { get; init; }

    [Id(6)]
    public required int UiFlags { get; init; }

    /// <summary>Chat font size step, 0-4. The client clamps it to that range and scales the bubble
    /// text by it; it arrives on SetChatStylePreference, whose style half is ignored.</summary>
    [Id(12)]
    public required int ChatSizePreference { get; init; }

    /// <summary>The player's own chat settings, which used to be the room's. Sent back on the
    /// account-preferences packet: a setting the server never echoes is a setting that resets on
    /// every login.</summary>
    [Id(13)]
    public required ChatModeType ChatMode { get; init; }

    [Id(14)]
    public required ChatBubbleWidthType ChatBubbleWidth { get; init; }

    [Id(15)]
    public required ChatScrollSpeedType ChatScrollSpeed { get; init; }

    /// <summary>Version of the Discord consent dialog the player answered; 0 = never answered, which
    /// is what makes the client show its opt-in popup.</summary>
    [Id(7)]
    public required int DiscordSettingsVersion { get; init; }

    [Id(8)]
    public required bool DiscordShowHabbo { get; init; }

    [Id(9)]
    public required bool DiscordShareActivity { get; init; }

    [Id(10)]
    public required bool DiscordHideInHiddenRooms { get; init; }

    [Id(11)]
    public required bool DiscordAllowJoining { get; init; }
}
