namespace Vortex.Dashboard.API.Infrastructure;

/// <summary>
/// One wearable, as <c>figuredata.xml</c> describes it.
/// </summary>
/// <remarks>
/// There is no name here because figuredata has none: a garment is a number under a type, and the
/// client's own avatar editor shows swatches rather than labels. Everything an operator can be given
/// to recognise one is in these four fields plus a rendered picture.
/// </remarks>
/// <param name="Type">The figure part type — <c>sh</c>, <c>ch</c>, <c>lg</c>, and twenty more.</param>
/// <param name="Membership">Whether it needs HC, which figuredata says as <c>club="2"</c>.</param>
/// <param name="Gender"><c>M</c>, <c>F</c> or <c>U</c>.</param>
/// <param name="Colour">
/// A colour to draw it in, from the palette its type uses, or null when the set is not colourable or
/// its type has no palette. Not the garment's own colour — a set has none, it is worn in whichever
/// colour the player picked — only one the picture can be drawn in, so the rows are consistent.
/// </param>
internal sealed record FigureSet(int Id, string Type, bool Membership, string Gender, int? Colour);
