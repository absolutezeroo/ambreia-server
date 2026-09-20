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
/// <param name="Club">Whether it needs HC, which figuredata says as <c>club="2"</c>.</param>
/// <param name="Gender"><c>M</c>, <c>F</c> or <c>U</c>.</param>
internal sealed record FigureSet(int Id, string Type, bool Club, string Gender);
