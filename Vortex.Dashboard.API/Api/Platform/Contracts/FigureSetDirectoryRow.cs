namespace Vortex.Dashboard.API.Api.Platform.Contracts;

/// <summary>
/// One garment in the picker.
/// </summary>
/// <remarks>
/// It has no name because figuredata has none — a wearable is a number under a part type, and the
/// client's avatar editor shows swatches rather than labels. The picture is therefore not a nicety:
/// without it the type and the number are all an operator has to go on.
/// </remarks>
/// <param name="Type">The figure part type: <c>sh</c>, <c>ch</c>, <c>lg</c> and twenty more.</param>
/// <param name="Membership">Whether it needs HC.</param>
/// <param name="Gender"><c>M</c>, <c>F</c> or <c>U</c>.</param>
/// <param name="ImageUrl">Null when no whole-avatar template is configured.</param>
public sealed record FigureSetDirectoryRow(
    int Id,
    string Type,
    bool Membership,
    string Gender,
    string? ImageUrl
);
