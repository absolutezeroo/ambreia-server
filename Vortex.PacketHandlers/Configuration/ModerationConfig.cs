namespace Vortex.PacketHandlers.Configuration;

/// <summary>
/// Config keys and defaults for moderation-tool limits, served live from <c>IServerConfigGrain</c>
/// (migrated off IOptions/appsettings). The default is the fallback when a key has no admin override
/// stored in the DB.
/// </summary>
public static class ModerationConfig
{
    /// <summary>
    /// The staff CFH tool's mute action carries no duration on the wire (unlike the in-room mute
    /// panel, which sends explicit minutes) — this is the server-side default applied for it.
    /// </summary>
    public const string ModToolDefaultMuteMinutesKey = "moderation.modtool_default_mute_minutes";
    public const int ModToolDefaultMuteMinutesDefault = 60;

    /// <summary>Max chat lines returned per room for GetRoomChatlogMessageHandler.</summary>
    public const string RoomChatlogLimitKey = "moderation.room_chatlog_limit";
    public const int RoomChatlogLimitDefault = 100;

    /// <summary>Max distinct rooms returned for GetUserChatlogMessageHandler.</summary>
    public const string UserChatlogRoomLimitKey = "moderation.user_chatlog_room_limit";
    public const int UserChatlogRoomLimitDefault = 10;

    /// <summary>Max chat lines per room for GetUserChatlogMessageHandler.</summary>
    public const string UserChatlogMessagesPerRoomKey = "moderation.user_chatlog_messages_per_room";
    public const int UserChatlogMessagesPerRoomDefault = 50;

    /// <summary>Max visit rows returned for GetRoomVisitsMessageHandler. The client's list has no
    /// date column, so a window much wider than a session is not useful to a moderator anyway.</summary>
    public const string RoomVisitLimitKey = "moderation.room_visit_limit";
    public const int RoomVisitLimitDefault = 50;
}
