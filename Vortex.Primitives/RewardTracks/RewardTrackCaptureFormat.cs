namespace Vortex.Primitives.RewardTracks;

/// <summary>
/// How a sequence's captured facts are written on a player's task row.
/// </summary>
/// <remarks>
/// <para>
/// One entry per fact, three fields each: the step that recorded it, the fact key, the value.
/// ASCII record and unit separators, so nothing an operator can type collides with them.
/// </para>
/// <para>
/// Here rather than beside the engine that writes it because the dashboard reads it too, to answer
/// "what has this player actually matched so far". Two copies of a separator is how a format comes
/// to be read one way and written another, and the symptom is a column of replacement characters
/// on a screen nobody looks at twice.
/// </para>
/// </remarks>
public static class RewardTrackCaptureFormat
{
    /// <summary>Between one captured fact and the next.</summary>
    public const char PairSeparator = '\u001E';

    /// <summary>Between the step, the key and the value of one capture.</summary>
    public const char FieldSeparator = '\u001F';
}
