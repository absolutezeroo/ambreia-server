namespace Vortex.Primitives.Sound;

/// <summary>
/// The client's own logic names for the sound furniture, as they appear in the assets and therefore
/// in <c>furniture_definitions.logic</c>.
/// </summary>
/// <remarks>
/// A definition is recognised as a song disk by this name and by nothing else — there is no flag and
/// no category for it — so the string is written once here rather than retyped wherever the question
/// is asked. A typo would not fail: it would quietly make every disk in the hotel an ordinary furni.
/// </remarks>
public static class SoundLogicNames
{
    public const string SongDisk = "furniture_song_disk";
}
