using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Protocol.Messages.Outgoing.Preferences;

namespace Vortex.PacketHandlers.Preferences;

/// <summary>
/// Builds the one composer both Discord handlers answer with — the client has no separate "saved"
/// reply and keeps whatever the last preferences event carried.
/// </summary>
internal static class DiscordPreferences
{
    public static DiscordPreferencesEventMessageComposer Build(
        PlayerAccountPreferencesSnapshot prefs
    ) =>
        new()
        {
            Version = prefs.DiscordSettingsVersion,
            ShowHabbo = prefs.DiscordShowHabbo,
            ShareActivity = prefs.DiscordShareActivity,
            HideInHiddenRooms = prefs.DiscordHideInHiddenRooms,
            AllowJoining = prefs.DiscordAllowJoining,
        };
}
