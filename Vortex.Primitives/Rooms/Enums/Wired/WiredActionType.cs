namespace Vortex.Primitives.Rooms.Enums.Wired;

public enum WiredActionType
{
    TOGGLE_FURNI_STATE = 0,
    RESET = 1,

    /// <summary>
    /// Restores the furni to the position, state, direction and altitude they had when the box was
    /// saved.
    /// </summary>
    /// <remarks>
    /// Named SET_FURNI_STATE here until 2026-09-13, which was a guess at an obfuscated constant and
    /// the wrong one. The client's class for code 3 declares <c>hasStateSnapshot</c> and four
    /// checkboxes — state, direction, position, altitude — and Habbo's own documentation calls it
    /// "match furni to position and state": it restores four aspects, it does not set one.
    /// </remarks>
    MATCH_TO_SNAPSHOT = 3,
    MOVE_AND_ROTATE_FURNI = 4,
    GIVE_SCORE = 6,
    CHAT = 7,
    TELEPORT = 8,
    JOIN_TEAM = 9,
    LEAVE_TEAM = 10,
    CHASE = 11,
    FLEE = 12,
    MOVE_TO_DIRECTION = 13,
    GIVE_SCORE_TO_PREDEFINED_TEAM = 14,
    TOGGLE_TO_RANDOM_STATE = 15,
    MOVE_FURNI_TO = 16,
    GIVE_REWARD = 17,
    CALL_ANOTHER_STACK = 18,
    KICK_FROM_ROOM = 19,
    MUTE_USER = 20,
    BOT_TELEPORT = 21,
    BOT_MOVE = 22,
    BOT_TALK = 23,
    BOT_GIVE_HAND_ITEM = 24,
    BOT_FOLLOW_AVATAR = 25,
    BOT_CHANGE_FIGURE = 26,
    BOT_TALK_DIRECT_TO_AVTR = 27,
    CONTROL_CLOCK = 28,
    SET_FURNI_ALTITUDE = 29,
    SEND_SIGNAL = 30,
    FREEZE_USER = 31,
    UNFREEZE_USER = 32,
    RELATIVE_FURNI_MOVE = 33,
    MOVE_FURNI_TO_FURNI = 34,
    MOVE_FURNI_TO_USER = 35,
    NEG_CALL_ANOTHER_STACK = 36,
    NEG_SEND_SIGNAL = 37,
    ADJUST_CLOCK = 38,
    GIVE_VARIABLE = 39,
    REMOVE_VARIABLE = 40,
    CHANGE_VARIABLE = 41,
    MOVE_USER = 42,
    MOVE_USER_TO_FURNI = 43,

    /// <summary>Pays credits out of a wired chest the box points at.</summary>
    GIVE_CURRENCY_FROM_CHEST = 45,

    /// <summary>Hands furniture out of a wired chest the box points at.</summary>
    GIVE_FURNI_FROM_CHEST = 46,

    /// <summary>Offers a contract to the resolved users.</summary>
    INITIATE_TRANSACTION = 47,

    /// <summary>Calls off a contract that is waiting on someone.</summary>
    CANCEL_TRANSACTION = 48,
    GIVE_EFFECT = 52,

    /// <summary>Writes a line of the builder's own text into the room's wired log.</summary>
    WRITE_TO_LOG = 49,

    /// <summary>The same line, on the branch where the conditions did not hold.</summary>
    NEG_WRITE_TO_LOG = 50,

    /// <summary>Advances a wired achievement (<c>WF_*</c>) for the resolved users.</summary>
    PROGRESS_ACHIEVEMENT = 51,

    /// <summary>Takes the selected furni out of the room, into their owner's inventory.</summary>
    REMOVE_FURNI = 56,

    /// <summary>Moves several furni at once, keeping the arrangement between them.</summary>
    MOVE_AS_GROUP = 57,
}
