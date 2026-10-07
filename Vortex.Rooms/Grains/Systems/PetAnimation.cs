using System.Globalization;

namespace Vortex.Rooms.Grains.Systems;

/// <summary>
/// What the client is told to draw around a pet: the postures its own asset declares and the status
/// segments that carry them.
/// </summary>
/// <remarks>
/// Its own type because every value here is answerable only by the client — an asset's animation
/// id, a parser's rule about what it keeps, a capture of the live hotel — while the rest of
/// <see cref="RoomPetRuntime" /> is rules over a <c>PetSnapshot</c> that we decide ourselves. A
/// posture invented here is not wrong in a way any test can see: the client silently falls back to
/// standing, so the evidence has to travel with the value.
/// </remarks>
internal static class PetAnimation
{
    /// <summary>
    /// What a pet is doing when it is doing nothing. The pet asset's own default animation id, and
    /// what the client falls back to for any posture it does not recognise.
    /// </summary>
    public const string StandPosture = "std";

    /// <summary>The posture a pet takes to sleep. One of the ids the pet's own asset declares.</summary>
    public const string LayPosture = "lay";

    /// <summary>Playing with a toy. Declared by the pet assets as `pla`.</summary>
    public const string PlayPosture = "pla";

    /// <summary>
    /// A status segment the client will actually keep.
    /// </summary>
    /// <remarks>
    /// The client drops any segment that carries no value: it splits on a space and only records the
    /// action when there are two pieces (<c>if (pieces.length >= 2)</c> in the nitro parser, which is
    /// what this revision speaks). So a bare <c>/lay/</c> never becomes a posture at
    /// all, and the pet stays standing -- no lying down, no Zzz. Arcturus never sends one
    /// either: it passes the tile's stack height. The height doubles as the value the client reads
    /// back for <c>lay</c> and <c>sit</c>, so it has to be the real one, formatted invariantly.
    /// </remarks>
    public static string StatusWithHeight(string posture, double height) =>
        $"/{posture} {height.ToString("0.##", CultureInfo.InvariantCulture)}/";

    public static string LayStatus(double height) => StatusWithHeight(LayPosture, height);

    public static string PlayStatus(double height) => StatusWithHeight(PlayPosture, height);
}
